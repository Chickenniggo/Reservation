using Reservations.Api;
using Xunit;

namespace Reservations.Api.Tests;

public sealed class ReservationInputTests
{
    private static ReservationInput ValidInput => new()
    {
        Name = "Max Mustermann",
        RoomId = Guid.NewGuid(),
        From = new DateOnly(2026, 11, 1),
        To = new DateOnly(2026, 11, 3)
    };

    [Fact]
    public void ValidReservationIsAccepted() => ValidInput.Validate();

    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    public void InvalidDateRangeIsRejected(int startDay)
    {
        var input = ValidInput with { From = new DateOnly(2026, 11, startDay), To = new DateOnly(2026, 11, 1) };
        Assert.Equal("invalid_date_range", Assert.Throws<ApiException>(() => input.Validate()).Code);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" Max")]
    [InlineData("Max  Mustermann")]
    [InlineData("Max\n")]
    public void InvalidNameIsRejected(string name)
    {
        Assert.Equal("schema_violation", Assert.Throws<ApiException>(() => (ValidInput with { Name = name }).Validate()).Code);
    }

    [Fact]
    public void EmptyRoomIsRejected()
    {
        Assert.Throws<ApiException>(() => (ValidInput with { RoomId = Guid.Empty }).Validate());
    }
}