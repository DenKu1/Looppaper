using Looppaper.Core.Media;

namespace Looppaper.Core.Tests;

public class MediaSequenceTests
{
    private static readonly string[] Five = ["a", "b", "c", "d", "e"];

    [Fact]
    public void EmptySequenceReturnsNull()
    {
        var sequence = new MediaSequence(shuffle: false);

        Assert.Null(sequence.Next());
        sequence.Update([]);
        Assert.Null(sequence.Next());
    }

    [Fact]
    public void OrderedWrapsAround()
    {
        var sequence = new MediaSequence(shuffle: false);
        sequence.Update(["a", "b", "c"]);

        var taken = Enumerable.Range(0, 7).Select(_ => sequence.Next()!).ToArray();

        Assert.Equal(["a", "b", "c", "a", "b", "c", "a"], taken);
    }

    [Fact]
    public void OrderedUpdateContinuesAfterCurrent()
    {
        var sequence = new MediaSequence(shuffle: false);
        sequence.Update(["a", "c", "e"]);
        sequence.Next();
        sequence.Next(); // "c"

        sequence.Update(["a", "b", "d", "e"]);

        Assert.Equal("d", sequence.Next());
        Assert.Equal("e", sequence.Next());
        Assert.Equal("a", sequence.Next());
    }

    [Fact]
    public void OrderedUpdateWithSameItemsKeepsPosition()
    {
        var sequence = new MediaSequence(shuffle: false);
        sequence.Update(["a", "b", "c"]);
        sequence.Next();

        sequence.Update(["a", "b", "c"]);

        Assert.Equal("b", sequence.Next());
    }

    [Fact]
    public void ShuffleVisitsEveryItemOncePerPass()
    {
        var sequence = new MediaSequence(shuffle: true, new Random(1234));
        sequence.Update(Five);

        for (var pass = 0; pass < 20; pass++)
        {
            var taken = Enumerable.Range(0, Five.Length).Select(_ => sequence.Next()!).ToList();
            Assert.Equal(Five, taken.Order(StringComparer.Ordinal));
        }
    }

    [Fact]
    public void ShuffleNeverRepeatsImmediatelyAcrossPasses()
    {
        for (var seed = 0; seed < 50; seed++)
        {
            var sequence = new MediaSequence(shuffle: true, new Random(seed));
            sequence.Update(["x", "y", "z"]);
            string? previous = null;
            for (var i = 0; i < 60; i++)
            {
                var next = sequence.Next();
                Assert.NotEqual(previous, next);
                previous = next;
            }
        }
    }

    [Fact]
    public void ShuffleActuallyChangesOrder()
    {
        var sequence = new MediaSequence(shuffle: true, new Random(7));
        sequence.Update(Enumerable.Range(0, 20).Select(i => i.ToString("D2", System.Globalization.CultureInfo.InvariantCulture)).ToArray());

        var pass = Enumerable.Range(0, 20).Select(_ => sequence.Next()!).ToArray();

        Assert.NotEqual(sequence.Items, pass);
    }

    [Fact]
    public void SingleItemRepeats()
    {
        var sequence = new MediaSequence(shuffle: true, new Random(3));
        sequence.Update(["only"]);

        Assert.Equal("only", sequence.Next());
        Assert.Equal("only", sequence.Next());
    }

    [Fact]
    public void ShuffleUpdateAvoidsRepeatingCurrent()
    {
        for (var seed = 0; seed < 50; seed++)
        {
            var sequence = new MediaSequence(shuffle: true, new Random(seed));
            sequence.Update(["a", "b"]);
            var current = sequence.Next();

            sequence.Update(["a", "b", "c"]);

            Assert.NotEqual(current, sequence.Next());
        }
    }
}
