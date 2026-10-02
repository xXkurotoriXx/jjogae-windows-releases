using System.Text.Json;

namespace Jjogae.Core;

public sealed record ChzzkSubscription(bool Active, string Tier, string Renewal)
{
    public static ChzzkSubscription Parse(JsonElement root)
    {
        var info = J.At(root, "content", "info");
        var channel = J.String(info, "channelId");
        if (channel.Length > 0 && channel != Channel.Id) throw new InvalidDataException("구독 채널이 일치하지 않습니다.");
        var tier = J.String(info, "tierName").Trim();
        var renewal = J.String(info, "nextPublishYmdt");
        var status = J.String(info, "status").ToUpperInvariant();
        var active = status is not ("NONE" or "EXPIRED")
            && (tier.Length > 0 || J.Number(J.At(info, "tierNo")) > 0 || renewal.Length > 0);
        return new(active, active ? tier.Length > 0 ? tier : "구독 중" : "구독하지 않음",
            active ? renewal.Split('T', ' ').FirstOrDefault() ?? "" : "");
    }
}
