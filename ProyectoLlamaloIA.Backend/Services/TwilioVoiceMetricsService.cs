using System.Globalization;
using PhoneNumbers;
using ProyectoLlamaloIA.Backend.Models;
using Twilio.Clients;
using Twilio.Rest.Api.V2010.Account;
using RecordingResource = Twilio.Rest.Api.V2010.Account.RecordingResource;

namespace ProyectoLlamaloIA.Backend.Services;

public interface ITwilioVoiceMetricsService
{
    Task<VoiceSummaryDto> GetSummaryAsync(string accountSid, string authToken, DateTime? fromUtc, DateTime? toUtc);
    Task<VoiceQualityAggDto> GetQualityAggAsync(string accountSid, string authToken, DateTime? fromUtc, DateTime? toUtc);
    Task<ConversationsHistoryDto> GetHistoryByCallerAsync(string accountSid, string authToken, string callerE164, DateTime? fromUtc, DateTime? toUtc);
    Task<(double minutes, string unit)> GetInboundMinutesThisMonthAsync(string accountSid, string authToken);
}

public class TwilioVoiceMetricsService : ITwilioVoiceMetricsService
{
    private readonly PhoneNumberUtil _phoneUtil = PhoneNumberUtil.GetInstance();

    private TwilioRestClient CreateClient(string sid, string token)
        => new TwilioRestClient(sid, token);

    public async Task<VoiceSummaryDto> GetSummaryAsync(string accountSid, string authToken, DateTime? fromUtc, DateTime? toUtc)
    {
        var client = CreateClient(accountSid, authToken);
        var rangeFrom = fromUtc ?? DateTime.UtcNow.AddYears(-10);
        var rangeTo   = toUtc   ?? DateTime.UtcNow;

        var calls = await CallResource.ReadAsync(
            startTimeAfter: rangeFrom,
            startTimeBefore: rangeTo,
            limit: 1000,
            client: client
        );

        var list        = calls.ToList();
        var totalCalls  = list.Count;
        var minutesUsed = list.Select(ParseDurationSec).Sum() / 60.0;

        // proxy de "time saved" basado en ring-time
        var timeSavedMinutes = list
            .Select(c =>
            {
                var start  = (c.StartTime ?? c.DateCreated)?.ToUniversalTime();
                var end    =  c.EndTime?.ToUniversalTime();
                var durSec =  ParseDurationSec(c);
                if (start == null || end == null) return 0.0;
                var ringMs = (end.Value - start.Value).TotalMilliseconds - (durSec * 1000.0);
                return ringMs > 0 ? ringMs : 0.0;
            })
            .Sum() / 60000.0;

        var last7        = DateTime.UtcNow.AddDays(-7);
        var last7dCalls  = list.Count(c => (c.StartTime ?? c.DateCreated)?.ToUniversalTime() >= last7);

        return new VoiceSummaryDto(totalCalls, minutesUsed, timeSavedMinutes, last7dCalls);
    }

    public async Task<VoiceQualityAggDto> GetQualityAggAsync(string accountSid, string authToken, DateTime? fromUtc, DateTime? toUtc)
    {
        var client   = CreateClient(accountSid, authToken);
        var rangeFrom = fromUtc ?? DateTime.UtcNow.AddDays(-30);
        var rangeTo   = toUtc   ?? DateTime.UtcNow;

        var calls = await CallResource.ReadAsync(
            startTimeAfter: rangeFrom,
            startTimeBefore: rangeTo,
            limit: 1000,
            client: client
        );

        var list = calls.ToList();
        if (list.Count == 0)
            return new VoiceQualityAggDto(0, 0, 0, 0, new VoiceTypesDto(0, 0, 0, 0), new List<CountryShareDto>());

        var totalMinutes = list.Select(ParseDurationSec).Sum() / 60.0;

        var latenciesMs = list
            .Select(c =>
            {
                var start  = (c.StartTime ?? c.DateCreated)?.ToUniversalTime();
                var end    =  c.EndTime?.ToUniversalTime();
                var durSec =  ParseDurationSec(c);
                if (start == null || end == null) return (double?)null;
                var ringMs = (end.Value - start.Value).TotalMilliseconds - (durSec * 1000.0);
                return ringMs >= 0 ? ringMs : 0.0;
            })
            .Where(v => v.HasValue)
            .Select(v => v!.Value)
            .ToList();

        var avgLatencyMs        = latenciesMs.Count > 0 ? latenciesMs.Average() : 0.0;
        var avgCallDurationSec  = list.Select(ParseDurationSec).DefaultIfEmpty(0).Average();

        var inbound = list.Count(c => (c.Direction ?? "").StartsWith("inbound",  StringComparison.OrdinalIgnoreCase));
        var outbound= list.Count(c => (c.Direction ?? "").StartsWith("outbound", StringComparison.OrdinalIgnoreCase));
        var total   = Math.Max(1, inbound + outbound);

        var types = new VoiceTypesDto(
            InboundPercent:  100.0 * inbound  / total,
            OutboundPercent: 100.0 * outbound / total,
            InboundCount:    inbound,
            OutboundCount:   outbound
        );

        var byCountry = new Dictionary<string,int>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in list)
        {
            var from = c.From;
            if (string.IsNullOrWhiteSpace(from)) continue;
            try
            {
                var num    = _phoneUtil.Parse(from, null);
                var region = _phoneUtil.GetRegionCodeForNumber(num) ?? "UNK";
                byCountry[region] = byCountry.TryGetValue(region, out var v) ? v + 1 : 1;
            }
            catch
            {
                byCountry["UNK"] = byCountry.TryGetValue("UNK", out var v) ? v + 1 : 1;
            }
        }

        var countries = byCountry
            .OrderByDescending(kv => kv.Value)
            .Select(kv => new CountryShareDto(kv.Key, 100.0 * kv.Value / list.Count, kv.Value))
            .ToList();

        var timeSavedMinutes = latenciesMs.Sum() / 60000.0;

        return new VoiceQualityAggDto(totalMinutes, timeSavedMinutes, avgLatencyMs, avgCallDurationSec, types, countries);
    }

    public async Task<ConversationsHistoryDto> GetHistoryByCallerAsync(string accountSid, string authToken, string callerE164, DateTime? fromUtc, DateTime? toUtc)
    {
        var client    = CreateClient(accountSid, authToken);
        var rangeFrom = fromUtc ?? DateTime.UtcNow.AddDays(-90);
        var rangeTo   = toUtc   ?? DateTime.UtcNow;

        var calls = await CallResource.ReadAsync(
            startTimeAfter: rangeFrom,
            startTimeBefore: rangeTo,
            from: callerE164,
            limit: 1000,
            client: client
        );

        var items = new List<CallItemDto>();
        foreach (var call in calls.OrderByDescending(c => c.StartTime ?? c.DateCreated))
        {
            var start  = (call.StartTime ?? call.DateCreated)?.ToUniversalTime();
            var end    =  call.EndTime?.ToUniversalTime();
            var durSec =  ParseDurationSec(call);

            double? latency = null;
            if (start != null && end != null)
            {
                var ringMs = (end.Value - start.Value).TotalMilliseconds - (durSec * 1000.0);
                latency = ringMs >= 0 ? ringMs : 0.0;
            }

            var recs = await RecordingResource.ReadAsync(
                callSid: call.Sid,
                client: client
            );

            var recDtos = recs.Select(r => new CallRecordingDto(
                r.Sid,
                $"https://api.twilio.com/2010-04-01/Accounts/{accountSid}/Recordings/{r.Sid}",
                ParseRecordingDurationSec(r)
            )).ToList();

            items.Add(new CallItemDto(
                Sid: call.Sid,
                StartTimeUtc: (start ?? DateTime.MinValue),
                EndTimeUtc: end,
                Status: call.Status?.ToString() ?? "unknown",
                Direction: call.Direction ?? "unknown",
                DurationSec: durSec > 0 ? durSec : (int?)null,
                LatencyMs: latency,
                Price: ParsePrice(call.Price),
                Recordings: recDtos
            ));
        }

        return new ConversationsHistoryDto(callerE164, items);
    }

    public async Task<(double minutes, string unit)> GetInboundMinutesThisMonthAsync(string accountSid, string authToken)
    {
        var client = CreateClient(accountSid, authToken);

        var start = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1);
        var end   = DateTime.UtcNow;

        var usage = await Twilio.Rest.Api.V2010.Account.Usage.RecordResource.ReadAsync(
            category: Twilio.Rest.Api.V2010.Account.Usage.RecordResource.CategoryEnum.CallsInbound,
            startDate: start,
            endDate: end,
            client: client
        );

        var first = usage.FirstOrDefault();
        if (first == null) return (0, "minutes");

        if (!double.TryParse(first.Usage, NumberStyles.Float, CultureInfo.InvariantCulture, out var val))
            val = 0;

        return (val, first.UsageUnit ?? "minutes");
    }

    // ---------- Helpers internos ----------

    private static int ParseDurationSec(CallResource c)
    {
        // En algunos SDKs es string? → parseo seguro
        if (int.TryParse(c.Duration, NumberStyles.Integer, CultureInfo.InvariantCulture, out var s))
            return s;
        return 0;
    }

    private static int? ParseRecordingDurationSec(RecordingResource r)
    {
        if (int.TryParse(r.Duration, NumberStyles.Integer, CultureInfo.InvariantCulture, out var s))
            return s;
        return null;
    }
    
    private static decimal? ParsePrice(object? price)
    {
        if (price is null) return null;
        if (price is decimal d) return d;
        if (price is string s && decimal.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var val))
            return val;
        return null;
    }
}