using System.Diagnostics;
using System.Windows.Threading;
using Microsoft.Win32;

namespace Jjogae.Windows;

public sealed class AppController : IDisposable
{
    public StateStore Store { get; }
    public AppState State { get; private set; }
    public string Status { get; private set; } = "아직 확인하지 않음";
    public bool Busy { get; private set; }
    public bool IsTest { get; }
    public long DataRevision { get; private set; }
    internal BroadcastThumbnails Thumbnails { get; }
    public CafeHistoryResult? Recovery { get; private set; }
    public bool RecoveryBusy { get; private set; }
    private CancellationTokenSource? recoveryCancellation;
    private List<Cheese>? indexedCheese;
    private CheeseCalendarIndex cheeseIndex = new([]);
    private DateOnly lastDay = Channel.Today;
    public CheeseCalendarIndex CheeseIndex
    {
        get { if (!ReferenceEquals(indexedCheese, State.Cheese)) { indexedCheese = State.Cheese; cheeseIndex = new(State.Cheese); } return cheeseIndex; }
    }
    public event Action? Changed;
    public event Func<Notification, bool>? Notify;
    private readonly NotificationDelivery notificationDelivery = new();
    private readonly ApiClient api = new();
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(15) };
    private readonly SemaphoreSlim cafeGate = new(1, 1);
    private DateTimeOffset lastFull = DateTimeOffset.MinValue;
    private DateTimeOffset lastCafe = DateTimeOffset.MinValue;
    private CancellationTokenSource lifetime = new();
    private int authenticationGeneration;
    private LoginWindow? login;
    private bool restoringLogin, loginRestoreDisabled;
    private DateTimeOffset lastLoginRestore;
    private YouTubeWindow? youtube;
    private readonly Dictionary<string, DateTimeOffset> cafeChecks = [];

    public AppController(string directory, bool test)
    {
        IsTest = test; Store = new StateStore(directory); State = Store.Load();
        Thumbnails = new BroadcastThumbnails(directory);
        if (!test) { CafeAvailability.Purge(State, State.Cafe.Where(x => !x.Notice).Select(x => x.Id).ToArray()); Store.Save(State); }
        timer.Tick += async (_, _) =>
        {
            if (State.Account is null && DateTimeOffset.UtcNow - lastLoginRestore >= TimeSpan.FromMinutes(5)) _ = RestoreLogin();
            if ((DateTimeOffset.UtcNow - lastFull).TotalSeconds >= State.Settings.RefreshSeconds) _ = Refresh();
            if (State.Settings.Notifications && State.Account is not null && (DateTimeOffset.UtcNow - lastCafe).TotalSeconds >= 60) await RefreshCafe();
            var notificationCount = State.NotificationKeys.Count;
            Policies.CalendarMilestones(State, DateTimeOffset.UtcNow);
            if (notificationCount != State.NotificationKeys.Count) Save();
            Drain();
            if (lastDay != Channel.Today) { lastDay = Channel.Today; Changed?.Invoke(); }
        };
        SystemEvents.PowerModeChanged += PowerChanged;
    }
    public void Start() { if (!IsTest) { timer.Start(); _ = RestoreLogin(); _ = Refresh(); if (State.Settings.YouTubeWebEnabled) _ = YouTubeSession().Refresh(); } }
    private YouTubeWindow YouTubeSession() => youtube ??= new YouTubeWindow(Store.DirectoryPath, value =>
    {
        if (!State.Settings.YouTubeWebEnabled || State.Settings.YouTubeResetPending) return;
        State.YouTubeWeb = State.YouTubeWeb?.Merge(value) ?? value;
        if (value.Subscribers is { } count) { State.YouTubeSubscribers = count; State.YouTubeCheckedAt = value.CheckedAt; }
        Save();
    });
    public bool YouTubeDisconnecting { get; private set; }
    internal Func<Task>? YouTubeCleanupTest { get; set; }
    public async Task ShowYouTube(Window owner)
    {
        if (IsTest || YouTubeDisconnecting) return;
        var window = YouTubeSession(); window.Owner = owner; window.Show(); window.Activate();
        if (State.Settings.YouTubeResetPending)
        {
            try { await window.SignOut().WaitAsync(TimeSpan.FromSeconds(15)); }
            catch { window.ShutDown(); youtube = null; throw new InvalidOperationException("저장된 YouTube 세션 정리가 끝나지 않았습니다. 로그인을 다시 눌러 주세요."); }
            State.Settings.YouTubeResetPending = false;
            window.ResumeReading();
        }
        State.Settings.YouTubeWebEnabled = true; Save();
        await window.Refresh(true);
    }
    public async Task DisconnectYouTube()
    {
        if (YouTubeDisconnecting) return;
        YouTubeDisconnecting = true;
        // Stop accepting observations before awaiting browser cleanup.
        State.Settings.YouTubeWebEnabled = false; State.Settings.YouTubeResetPending = true; State.YouTubeWeb = null; Save();
        var session = IsTest ? null : YouTubeSession();
        try
        {
            await (IsTest ? YouTubeCleanupTest?.Invoke() ?? Task.CompletedTask : session!.SignOut()).WaitAsync(TimeSpan.FromSeconds(8));
            State.Settings.YouTubeResetPending = false;
        }
        catch (Exception) { Report("YouTube 연결 해제 완료 · 다음 로그인 전에 저장된 세션을 정리합니다."); }
        finally { session?.ShutDown(); youtube = null; YouTubeDisconnecting = false; Save(); }
    }
    private void PowerChanged(object sender, PowerModeChangedEventArgs args)
    {
        if (args.Mode == PowerModes.Resume)
            Application.Current.Dispatcher.BeginInvoke(new Action(() => { lastFull = DateTimeOffset.MinValue; lastCafe = DateTimeOffset.MinValue; _ = Refresh(); }));
    }
    public void Save() { State.Settings.Normalize(); Store.Save(State); DataRevision++; Changed?.Invoke(); }
    private void Report(string value) { Status = value; Changed?.Invoke(); }

    public async Task Refresh(bool fullCheese = false)
    {
        if (Busy || IsTest) return;
        var cycleStartedAt = DateTimeOffset.UtcNow;
        Busy = true; lastFull = cycleStartedAt; Report("최신 정보를 확인하는 중…");
        if (State.Settings.YouTubeWebEnabled) _ = YouTubeSession().Refresh();
        var generation = authenticationGeneration; var token = lifetime.Token;
        var errors = new List<string>();
        try
        {
            var result = await api.Public(token);
            if (token.IsCancellationRequested) return;
            errors.AddRange(result.Errors);
            Policies.ObserveMedia(State, result.Media, result.Live, result.Followers, result.YouTube.Subscribers, DateTimeOffset.UtcNow);
            if (result.YouTube.Subscribers is not null) State.YouTubeCheckedAt = DateTimeOffset.UtcNow;
            if (result.YouTube.LatestVideo is { } latest) State.YouTubeLatestVideoId = latest.Id;
            foreach (var record in result.Broadcasts) Policies.MergeBroadcast(State, record);
            await RefreshBroadcastAvailability(token, cycleStartedAt);
            if (State.Account is { } account)
            {
                _ = RefreshCafe();
                try
                {
                    var details = await api.AccountDetails(account.Nickname, token);
                    if (generation == authenticationGeneration) State.Account = details with { UserId = account.UserId };
                }
                catch (Exception e) when (e is not OperationCanceledException) { errors.Add(e.Message); }
                try
                {
                    var profile = await api.ChatProfile(account, token);
                    if (generation == authenticationGeneration && profile.BelongsTo(State.Account)) State.ChatProfile = profile;
                }
                catch (Exception) when (!token.IsCancellationRequested)
                {
                    if (generation == authenticationGeneration && State.ChatProfile?.BelongsTo(State.Account) == true)
                        State.ChatProfile = State.ChatProfile with { RefreshError = "채팅 배지를 다시 확인하지 못했습니다." };
                }
                try
                {
                    var cheese = await api.CheeseHistory(fullCheese, token);
                    if (generation == authenticationGeneration) State.Cheese = cheese.Concat(State.Cheese).DistinctBy(x => x.Id).OrderByDescending(x => x.At).ToList();
                }
                catch (Exception e) when (e is not OperationCanceledException) { errors.Add("치즈: " + e.Message); }
            }
            State.LastUpdatedAt = DateTimeOffset.UtcNow;
            Policies.CalendarMilestones(State, DateTimeOffset.UtcNow); Save(); Drain();
            await RefreshBroadcastDetails(false, token);
            await SaveRemoteThumbnails(token);
            Report(errors.Count == 0 ? "방금 업데이트" : string.Join(" / ", errors));
        }
        catch (OperationCanceledException) { }
        catch (Exception e) { Report("확인 실패: " + e.Message); }
        finally { Busy = false; Changed?.Invoke(); }
    }

    public async Task RefreshCafe()
    {
        if (IsTest || State.Account is null || !await cafeGate.WaitAsync(0)) return;
        var generation = authenticationGeneration; var token = lifetime.Token;
        try
        {
            lastCafe = DateTimeOffset.UtcNow;
            var articles = await api.Cafe(token: token);
            if (generation != authenticationGeneration || token.IsCancellationRequested) return;
            Policies.MergeCafe(State, articles.Where(x => x.Notice), DateTimeOffset.UtcNow); Save();
            var listed = articles.Select(x => x.Id).ToHashSet();
            var now = DateTimeOffset.UtcNow;
            var candidates = State.Cafe.Where(x => !listed.Contains(x.Id) && CafeAvailability.Url(x.Id) is not null
                && (!cafeChecks.TryGetValue(x.Id, out var check) || now - check >= TimeSpan.FromMinutes(10)))
                .OrderBy(x => cafeChecks.GetValueOrDefault(x.Id)).Take(3).Select(x => x.Id).ToArray();
            foreach (var id in candidates)
            {
                cafeChecks[id] = now;
                try
                {
                    var deleted = await api.CafeDeleted(id, token);
                    if (generation != authenticationGeneration || token.IsCancellationRequested) return;
                    if (!deleted) continue;
                    CancelRecovery(); CafeAvailability.Purge(State, [id]); cafeChecks.Remove(id); Save();
                }
                catch (Exception) when (!token.IsCancellationRequested) { /* A failed check never removes a post. */ }
            }
            Drain();
        }
        catch (OperationCanceledException) { }
        catch (Exception e) { Report("카페: " + e.Message); }
        finally { cafeGate.Release(); }
    }

    public void ShowLogin(Window owner)
    {
        loginRestoreDisabled = false;
        login ??= new LoginWindow(Store.DirectoryPath, VerifyLogin) { Owner = owner };
        login.Owner = owner;
        api.CookieHeader = login.Cookies;
        login.Show(); login.Activate();
    }
    private async Task RestoreLogin()
    {
        if (IsTest || restoringLogin || loginRestoreDisabled || State.Account is not null) return;
        lastLoginRestore = DateTimeOffset.UtcNow;
        if (!Directory.Exists(Path.Combine(Store.DirectoryPath, "WebView2"))) return;
        restoringLogin = true;
        var generation = authenticationGeneration;
        try
        {
            login ??= new LoginWindow(Store.DirectoryPath, VerifyLogin);
            if (!await login.RestoreSession() || generation != authenticationGeneration) return;
            api.CookieHeader = login.Cookies;
            var account = await api.AccountIdentity(lifetime.Token);
            if (generation != authenticationGeneration || account is null) return;
            State.Account = account; DataRevision++; Changed?.Invoke();
            await RefreshCafe();
        }
        catch (OperationCanceledException) { }
        catch (Exception) { if (!lifetime.IsCancellationRequested) Report("카페 자동 연결을 확인하지 못했습니다. 설정에서 로그인을 확인해 주세요."); }
        finally { restoringLogin = false; }
    }
    private async Task VerifyLogin()
    {
        var generation = ++authenticationGeneration;
        CancelRecovery();
        var account = await api.AccountIdentity(lifetime.Token);
        if (generation != authenticationGeneration) return;
        if (account is null) throw new InvalidOperationException("네이버 로그인이 확인되지 않았습니다. 치지직 페이지에서 로그인해 주세요.");
        State.Account = account; DataRevision++;
        if (State.ChatProfile?.BelongsTo(account) != true) State.ChatProfile = null;
        Report("로그인을 확인했습니다.");
        await RefreshCafe(); await Refresh();
        if (generation != authenticationGeneration || lifetime.IsCancellationRequested) return;
        if (State.ChatProfile is { } currentProfile && currentProfile.BelongsTo(State.Account) && currentProfile.RefreshError.Length == 0) return;
        try
        {
            var profile = await api.ChatProfile(account, lifetime.Token);
            if (generation != authenticationGeneration || lifetime.IsCancellationRequested) return;
            if (!profile.BelongsTo(State.Account)) throw new InvalidDataException("채팅 프로필의 계정이 일치하지 않습니다.");
            State.ChatProfile = profile; Save(); Report("로그인과 채팅 프로필을 확인했습니다.");
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            throw new InvalidOperationException("로그인은 확인했지만 채팅 프로필을 가져오지 못했습니다. 잠시 후 다시 눌러 주세요. " + error.Message, error);
        }
    }
    public async Task Logout()
    {
        loginRestoreDisabled = true;
        authenticationGeneration++; CancelRecovery(); State.Account = null; State.ChatProfile = null; State.Pending.Clear();
        api.CookieHeader = null;
        if (login is not null) await login.SignOut();
        Save(); Report("로그아웃했습니다. 저장된 기록은 유지됩니다.");
    }
    public void CancelRecovery()
    {
        recoveryCancellation?.Cancel(); recoveryCancellation?.Dispose(); recoveryCancellation = null;
        Recovery = null; RecoveryBusy = false;
    }
    public CafeRecoveryCandidates? RecoveryCandidates => Recovery is { } result ? new(State, result.Query, result.Articles) : null;
    internal void SeedRecoveryCheck(CafeHistoryResult result)
    {
        if (!IsTest) throw new InvalidOperationException("Recovery fixtures require an isolated test profile.");
        Recovery = result; DataRevision++;
    }
    public async Task LoadCafeRecovery(CafeHistoryQuery query, bool continuing = false)
    {
        query = query with { Scope = CafeHistoryScope.Notice };
        if (State.Account is null && !IsTest) throw new InvalidOperationException("먼저 네이버 로그인을 확인해 주세요.");
        query.Validate(); if (RecoveryBusy) return;
        var previous = continuing && Recovery?.Query == query ? Recovery : null;
        if (continuing && previous?.NextPage is null) return;
        CancelRecovery(); Recovery = previous; RecoveryBusy = true;
        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token); recoveryCancellation = cancellation;
        var generation = authenticationGeneration;
        Report("과거 글을 조회하는 중…");
        try
        {
            var result = await CafeHistoryLoader.Fetch(query, previous?.NextPage ?? 1, api.CafeHistoryPage, cancellation.Token);
            if (generation != authenticationGeneration || cancellation.IsCancellationRequested || !ReferenceEquals(recoveryCancellation, cancellation)) return;
            Recovery = result with { Articles = result.Articles.Concat(previous?.Articles ?? []).DistinctBy(x => x.Id).OrderByDescending(x => x.At).ToArray(), PagesFetched = result.PagesFetched + (previous?.PagesFetched ?? 0) };
            Report("조회가 끝났습니다. 복구할 글을 선택해 주세요.");
        }
        catch (OperationCanceledException) { }
        finally { if (ReferenceEquals(recoveryCancellation, cancellation)) { RecoveryBusy = false; DataRevision++; Changed?.Invoke(); } }
    }
    public void RestoreCafe(IEnumerable<string> selected)
    {
        var count = RecoveryCandidates?.Restore(State, selected) ?? 0;
        Save(); Report($"{count:N0}개 글을 복구했습니다.");
    }
    public async Task LoadBroadcastHistory()
    {
        if (Busy) return;
        Busy = true; Report("다시보기를 확인하는 중…");
        var cycleStartedAt = DateTimeOffset.UtcNow;
        try
        {
            foreach (var record in await api.BroadcastHistory(lifetime.Token)) Policies.MergeBroadcast(State, record);
            await RefreshBroadcastAvailability(lifetime.Token, cycleStartedAt);
            Save(); await RefreshBroadcastDetails(true, lifetime.Token); await SaveRemoteThumbnails(lifetime.Token); Report("방송 기록을 보관했습니다.");
        }
        finally { Busy = false; Changed?.Invoke(); }
    }

    private async Task SaveRemoteThumbnails(CancellationToken token)
    {
        if (IsTest || !State.Settings.SaveThumbnails) return;
        var snapshot = State;
        foreach (var record in snapshot.Broadcasts.Where(x => !x.ThumbnailIsCustom && Thumbnails.PathFor(x) is null && ApiClient.SafeImage(x.ImageUrl)).Take(30).ToArray())
        {
            try
            {
                var bytes = await api.Image(record.ImageUrl, token);
                var filename = await Thumbnails.Save(record.Id, bytes);
                token.ThrowIfCancellationRequested();
                if (!ReferenceEquals(snapshot, State)) return;
                if (State.Broadcasts.FirstOrDefault(x => x.Id == record.Id) is { ThumbnailIsCustom: false } current) current.ThumbnailFilename = filename;
            }
            catch (OperationCanceledException) { throw; }
            catch { /* A failed image never removes an archived record or saved image. */ }
        }
        Save();
    }
    public void UpdateBroadcastTiming(string id, DateTimeOffset start, DateTimeOffset end)
    { State.Broadcasts = BroadcastTiming.Update(State.Broadcasts, id, start, end, DateTimeOffset.UtcNow); Save(); }
    private async Task RefreshBroadcastAvailability(CancellationToken token, DateTimeOffset cycleStartedAt)
    {
        if (IsTest) return;
        var snapshot = State;
        foreach (var id in BroadcastAvailability.Candidates(snapshot, cycleStartedAt))
        {
            ReplayAvailability availability;
            try { availability = await api.BroadcastAvailabilityCheck(id, token); }
            catch (Exception) when (!token.IsCancellationRequested) { availability = ReplayAvailability.Unknown; }
            token.ThrowIfCancellationRequested();
            if (!ReferenceEquals(snapshot, State)) return;
            var removed = BroadcastAvailability.Observe(State, id, availability, cycleStartedAt);
            Save();
            Thumbnails.RemoveUnreferenced(removed, State.Broadcasts);
        }
    }
    public void AddBroadcast(string title, DateTimeOffset start, DateTimeOffset end)
    { State.Broadcasts.Add(BroadcastTiming.Add(title, start, end, State.Broadcasts, DateTimeOffset.UtcNow)); Save(); }
    internal async Task SetThumbnail(string id, string path)
    {
        var snapshot = State;
        var filename = await Thumbnails.ImportImage(id, path);
        if (!ReferenceEquals(snapshot, State)) throw new InvalidOperationException("기록이 변경되었습니다. 다시 선택해 주세요.");
        var record = State.Broadcasts.Single(x => x.Id == id); record.ThumbnailFilename = filename; record.ThumbnailIsCustom = true; Save();
    }

    private void Drain()
    {
        if (IsTest || !State.Settings.Notifications || Notify is null) return;
        notificationDelivery.Drain(State, DateTimeOffset.UtcNow, Notify);
    }
    internal void RetryNotification(Notification entry)
    {
        if (State.Settings.Notifications && !entry.Id.StartsWith("test:", StringComparison.Ordinal) && State.Pending.All(x => x.Id != entry.Id)) State.Pending.Insert(0, entry);
    }
    public void SetNotifications(bool enabled)
    {
        State.Settings.Notifications = enabled; State.Pending.Clear(); Save();
        if (enabled) _ = RefreshCafe();
    }
    public void Open(string url) => OpenUrl(url, State.Settings.ExternalBrowser);
    public static void OpenUrl(string url, string browser = "system")
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != "https" || !uri.IsDefaultPort || !string.IsNullOrEmpty(uri.UserInfo)
            || !new[] { "chzzk.naver.com", "cafe.naver.com", "www.youtube.com", "developer.microsoft.com" }.Contains(uri.Host))
            throw new InvalidOperationException("허용되지 않은 링크입니다.");
        var executable = browser switch { "chrome" => "chrome.exe", "edge" => "msedge.exe", "firefox" => "firefox.exe", _ => null };
        if (executable is null) { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); return; }
        var key = @"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\" + executable;
        var path = Registry.GetValue(@"HKEY_CURRENT_USER\" + key, "", null) as string ?? Registry.GetValue(@"HKEY_LOCAL_MACHINE\" + key, "", null) as string;
        if (path is null || !File.Exists(path)) throw new InvalidOperationException("선택한 브라우저를 찾지 못했습니다. 설정에서 기본 브라우저를 선택해 주세요.");
        var process = new ProcessStartInfo(path) { UseShellExecute = false }; process.ArgumentList.Add(url); Process.Start(process);
    }
    public void OpenCafe(CafePost post) { Open(post.Url); Policies.OpenCafe(State, post); Save(); }
    public void ReadCafe(IEnumerable<string> ids) { Policies.MarkCafeRead(State, ids); Save(); }
    internal Task<byte[]> DisplayImage(string url)
    {
        if (IsTest) throw new InvalidOperationException("External images are disabled in the isolated smoke profile.");
        return api.Image(url, lifetime.Token);
    }
    private async Task RefreshBroadcastDetails(bool full, CancellationToken token)
    {
        var targets = State.Broadcasts.Where(x => BroadcastLogic.NeedsDetail(x, DateTimeOffset.UtcNow, full)).Take(full ? 200 : 10).ToArray();
        foreach (var batch in targets.Chunk(4))
        {
            var details = await Task.WhenAll(batch.Select(async record =>
            {
                try { return await api.BroadcastDetail(record.VideoId!, token); }
                catch (Exception) when (!token.IsCancellationRequested) { return null; }
            }));
            token.ThrowIfCancellationRequested();
            foreach (var record in batch)
                if (State.Broadcasts.FirstOrDefault(x => x.Id == record.Id) is { } current) current.DetailCheckedAt = DateTimeOffset.UtcNow;
            foreach (var record in details.OfType<Broadcast>()) Policies.MergeBroadcast(State, record);
        }
        if (targets.Length > 0) Save();
    }
    public void Import(string path)
    {
        if (Busy) throw new InvalidOperationException("새로고침이 끝난 뒤 가져와 주세요.");
        var imported = StateStore.Import(path);
        loginRestoreDisabled = true;
        authenticationGeneration++; CancelRecovery(); lifetime.Cancel(); lifetime.Dispose(); lifetime = new();
        CafeAvailability.Purge(imported, imported.Cafe.Where(x => !x.Notice).Select(x => x.Id).ToArray());
        imported.YouTubeWeb = null; imported.Settings.YouTubeWebEnabled = false;
        youtube?.ShutDown(); youtube = null; cafeChecks.Clear();
        State = imported; Save(); Report("Windows판 백업을 가져왔습니다. 로그인은 다시 확인해 주세요.");
    }
    internal Task<StorageUsage> ReadStorageUsage() => Task.Run(() => StorageUsage.Read(Store.DirectoryPath));
    public void Dispose()
    {
        timer.Stop(); SystemEvents.PowerModeChanged -= PowerChanged; CancelRecovery(); lifetime.Cancel(); login?.ShutDown(); youtube?.ShutDown(); api.Dispose(); lifetime.Dispose();
    }
}
