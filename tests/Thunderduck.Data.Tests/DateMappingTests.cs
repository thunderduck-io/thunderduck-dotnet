using Dapper;
using Xunit;

/// <summary>DATE/TIME/TIMESTAMPTZ surface as DateTime/TimeSpan/DateTime(UTC)
/// (Npgsql's convention) so Dapper and DataTable map them onto ordinary .NET
/// properties; DateOnly/TimeOnly/DateTimeOffset stay available via GetFieldValue.</summary>
public class DateMappingTests
{
    static FakeApi Api() => TestApi.Query(["day", "at", "ts", "tz"],
        ["DATE", "TIME", "TIMESTAMP", "TIMESTAMP WITH TIME ZONE"],
        ["2024-01-31", "13:45:00", "2024-01-31T10:00:00", "2024-01-31T12:00:00+02:00"]);

    public sealed record Row(DateTime Day, TimeSpan At, DateTime Ts, DateTime Tz);
    // Positional records need an exact constructor match in Dapper, so the
    // DateTimeOffset case uses settable properties (Dapper converts on assignment).
    public sealed class RowWithOffset { public DateTimeOffset Tz { get; set; } }

    [Fact]
    public void Dapper_maps_date_time_and_timestamptz_onto_DateTime_and_TimeSpan()
    {
        using var c = TestApi.Open(Api());
        var row = c.Query<Row>("SELECT day, at, ts, tz FROM t").Single();
        Assert.Equal(new DateTime(2024, 1, 31), row.Day);
        Assert.Equal(new TimeSpan(13, 45, 0), row.At);
        Assert.Equal(new DateTime(2024, 1, 31, 10, 0, 0), row.Ts);
        Assert.Equal(new DateTime(2024, 1, 31, 10, 0, 0, DateTimeKind.Utc), row.Tz);
        Assert.Equal(DateTimeKind.Utc, row.Tz.Kind);
    }

    [Fact]
    public void Dapper_maps_timestamptz_onto_DateTimeOffset()
    {
        using var c = TestApi.Open(Api());
        var row = c.Query<RowWithOffset>("SELECT tz FROM t").Single();
        Assert.Equal(new DateTimeOffset(2024, 1, 31, 10, 0, 0, TimeSpan.Zero), row.Tz);
    }

    [Fact]
    public void Reader_reports_provider_types_and_keeps_native_getters()
    {
        using var r = TestApi.Reader(Api());
        Assert.True(r.Read());
        Assert.Equal(typeof(DateTime), r.GetFieldType(0));
        Assert.Equal(typeof(TimeSpan), r.GetFieldType(1));
        Assert.Equal(typeof(DateTime), r.GetFieldType(3));
        Assert.Equal(new DateTime(2024, 1, 31), r.GetValue(0));
        Assert.Equal(new DateOnly(2024, 1, 31), r.GetFieldValue<DateOnly>(0));
        Assert.Equal(new TimeOnly(13, 45), r.GetFieldValue<TimeOnly>(1));
        Assert.Equal(new TimeSpan(13, 45, 0), r.GetFieldValue<TimeSpan>(1));
        // The original offset is preserved by the native getter.
        Assert.Equal(TimeSpan.FromHours(2), r.GetFieldValue<DateTimeOffset>(3).Offset);
        Assert.Equal(new DateTime(2024, 1, 31, 10, 0, 0, DateTimeKind.Utc), r.GetDateTime(3));
    }
}
