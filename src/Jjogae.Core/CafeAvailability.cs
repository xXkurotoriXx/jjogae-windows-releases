using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Jjogae.Core;

public static class CafeAvailability
{
    public static string? Url(string id) => id.Length is > 0 and <= 15 && id.All(c => c is >= '0' and <= '9') && long.TryParse(id, out var number) && number > 0
        ? $"https://article.cafe.naver.com/gw/v4/cafes/{Channel.CafeId}/articles/{id}?useCafeId=true&requestFrom=A" : null;
    public static bool IsDeleted(JsonElement root, int status)
    {
        if (status != 404 && (status < 200 || status >= 300)) return false;
        var result = J.At(root, "result");
        return J.String(result, "errorCode") == "4003" && J.String(result, "message") == "삭제되었거나 존재하지 않는 게시글입니다."
            && J.String(J.At(result, "more"), "cafeId") == Channel.CafeId;
    }
    public static void Purge(AppState state, IEnumerable<string> deleted)
    {
        var ids = deleted.ToHashSet();
        state.Cafe.RemoveAll(x => ids.Contains(x.Id));
        state.ReadCafe.ExceptWith(ids); state.SeenCafe.ExceptWith(ids);
        state.Pending.RemoveAll(x => x.Source == "카페" && ids.Contains(x.Id.StartsWith("cafe:") ? x.Id[5..] : ""));
        foreach (var id in ids) state.NotificationKeys.Remove(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("cafe:" + id))));
    }
}
