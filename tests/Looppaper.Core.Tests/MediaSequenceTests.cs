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

    [Fact]
    public void Previous_GoesBackThenNextReplaysForward()
    {
        var sequence = new MediaSequence(shuffle: false);
        sequence.Update(["a", "b", "c", "d"]);
        sequence.Next();
        sequence.Next();
        sequence.Next(); // "c"

        Assert.Equal("b", sequence.Previous());
        Assert.Equal("a", sequence.Previous());
        Assert.Equal("b", sequence.Next());
        Assert.Equal("c", sequence.Next());
        Assert.Equal("d", sequence.Next());
        Assert.Equal("a", sequence.Next());
    }

    [Fact]
    public void Previous_InOrder_KeepsGoingBackPastTheStart()
    {
        var sequence = new MediaSequence(shuffle: false);
        sequence.Update(["a", "b", "c"]);
        sequence.Next(); // "a"

        Assert.Equal("c", sequence.Previous());
        Assert.Equal("b", sequence.Previous());
        Assert.Equal("c", sequence.Next());
        Assert.Equal("a", sequence.Next());
        Assert.Equal("b", sequence.Next());
    }

    [Fact]
    public void Previous_Shuffled_RetracesWhatWasShown()
    {
        var sequence = new MediaSequence(shuffle: true, new Random(7));
        sequence.Update(Five);
        var shown = Enumerable.Range(0, 8).Select(_ => sequence.Next()!).ToList();

        for (var i = shown.Count - 2; i >= 0; i--)
        {
            Assert.Equal(shown[i], sequence.Previous());
        }

        Assert.Null(sequence.Previous()); // nothing older remembered
        Assert.Equal(shown[0], sequence.Current);
        Assert.Equal(shown.Skip(1), Enumerable.Range(0, shown.Count - 1).Select(_ => sequence.Next()!));
    }

    [Fact]
    public void Previous_SkipsRemovedItems()
    {
        var sequence = new MediaSequence(shuffle: false);
        sequence.Update(["a", "b", "c"]);
        sequence.Next();
        sequence.Next();
        sequence.Next(); // "c"

        sequence.Update(["a", "c"]);

        Assert.Equal("a", sequence.Previous());
        Assert.Equal("c", sequence.Next());
    }

    [Fact]
    public void Previous_NothingToGoBackTo()
    {
        var sequence = new MediaSequence(shuffle: false);
        Assert.Null(sequence.Previous());

        sequence.Update(["only"]);
        sequence.Next();
        Assert.Null(sequence.Previous());
    }

    [Fact]
    public void History_IsBounded()
    {
        var sequence = new MediaSequence(shuffle: true, new Random(1));
        sequence.Update(Five);
        for (var i = 0; i < MediaSequence.HistoryLimit + 50; i++)
        {
            sequence.Next();
        }

        var steps = 0;
        while (sequence.Previous() is not null)
        {
            steps++;
        }

        Assert.Equal(MediaSequence.HistoryLimit - 1, steps);
    }
}
