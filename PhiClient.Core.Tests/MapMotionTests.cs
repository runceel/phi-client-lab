using PhiClient.Core;

namespace PhiClient.Core.Tests;

public sealed class MapMotionTests
{
    [Fact]
    public void OnlyNearbySolidMovementInSameWorldSlidesForNinetyMilliseconds()
    {
        var initial = new MapMotion().Update(new(5, 5), "A", 0, true);
        Assert.Equal((0, 0), initial.Offset);
        var move = initial.Update(new(6, 5), "A", 100, true);
        Assert.Equal((32, 0), move.Offset);
        Assert.Equal((16, 0), move.Update(new(6, 5), "A", 145, true).Offset);
        Assert.Equal((0, 0), move.Update(new(6, 5), "A", 190, true).Offset);
        Assert.Equal((0, 0), initial.Update(new(6, 5), "B", 100, true).Offset);
        Assert.Equal((0, 0), initial.Update(new(8, 5), "A", 100, true).Offset);
        Assert.Equal((0, 0), initial.Update(new(6, 5), "A", 100, false).Offset);
        var diagonal = initial.Update(new(4, 3), "A", 100, true);
        Assert.Equal((-32, -64), diagonal.Offset);
        Assert.Equal((0, 0), diagonal.Stop().Offset);
    }

    [Fact]
    public void SlidingRasterMovesLiveAndRecalledCellsTogetherWithoutTouchingNeighborAlpha()
    {
        var pixels = Enumerable.Repeat((byte)200, 512 * 96 * 4).ToArray();
        for (var i = 3; i < pixels.Length; i += 4) pixels[i] = 255;
        var image = MapGraphics.Draw(1, 1, [(byte)' '], [0], [], _ => new(512, 96, pixels),
            null, _ => null, recalled: [true], slideX: 8);
        var left = (16 * image.Width) * 4;
        Assert.Equal(new byte[] { 0, 0, 0, 255 }, image.Pixels[left..(left + 4)]);
        Assert.Equal(new byte[] { 150, 150, 150, 255 }, image.Pixels[(left + 8 * 4)..(left + 9 * 4)]);
    }
}
