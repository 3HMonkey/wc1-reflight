using WingCommander.Audio.Dos;

namespace WingCommander.Audio.Tests.Dos;

public class AudioCommandQueueTests
{
    [Fact]
    public void Capacity_MustBeAPowerOfTwo()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new AudioCommandQueue(3));
        Assert.Throws<ArgumentOutOfRangeException>(() => new AudioCommandQueue(0));
        Assert.Equal(8, new AudioCommandQueue(8).Capacity);
    }

    [Fact]
    public void Commands_ComeOutInOrder_AcrossManyWrapArounds()
    {
        var queue = new AudioCommandQueue(4);
        int next = 0;
        for (int round = 0; round < 100; round++)
        {
            for (int i = 0; i < 3; i++)
                Assert.True(queue.TryEnqueue(new AudioCommand { Kind = AudioCommandKind.PlaySoundEffect, Arg0 = round * 3 + i }));
            for (int i = 0; i < 3; i++)
            {
                Assert.True(queue.TryDequeue(out var command));
                Assert.Equal(next++, command.Arg0);
            }
            Assert.False(queue.TryDequeue(out _));
        }
    }

    [Fact]
    public void FullQueue_RejectsUntilTheConsumerCatchesUp()
    {
        var queue = new AudioCommandQueue(2);
        Assert.True(queue.TryEnqueue(new AudioCommand { Arg0 = 1 }));
        Assert.True(queue.TryEnqueue(new AudioCommand { Arg0 = 2 }));
        Assert.False(queue.TryEnqueue(new AudioCommand { Arg0 = 3 }));
        Assert.Equal(2, queue.Count);
        Assert.True(queue.TryDequeue(out var first));
        Assert.Equal(1, first.Arg0);
        Assert.True(queue.TryEnqueue(new AudioCommand { Arg0 = 3 }));
        Assert.True(queue.TryDequeue(out var second));
        Assert.True(queue.TryDequeue(out var third));
        Assert.Equal((2, 3), (second.Arg0, third.Arg0));
    }

    [Fact]
    public void Dequeue_DropsTheMusicReferenceFromTheCell()
    {
        var queue = new AudioCommandQueue(2);
        Assert.True(queue.TryEnqueue(new AudioCommand { Kind = AudioCommandKind.SetMusic, Music = null, Arg0 = 7 }));
        Assert.True(queue.TryDequeue(out var command));
        Assert.Equal(AudioCommandKind.SetMusic, command.Kind);
        Assert.Equal(0, queue.Count);
    }

    [Fact]
    public async Task ConcurrentProducers_AndOneConsumer_DeliverEveryCommandOnce()
    {
        var queue = new AudioCommandQueue(64);
        const int producers = 4;
        const int perProducer = 20000;
        var received = new int[producers * perProducer];
        var consumer = Task.Run(() =>
        {
            int count = 0;
            while (count < received.Length)
            {
                if (queue.TryDequeue(out var command))
                {
                    received[command.Arg0]++;
                    count++;
                }
            }
        });
        Parallel.For(0, producers, p =>
        {
            for (int i = 0; i < perProducer; i++)
            {
                var command = new AudioCommand { Arg0 = p * perProducer + i };
                while (!queue.TryEnqueue(command))
                    Thread.SpinWait(10);
            }
        });
        await consumer.WaitAsync(TimeSpan.FromSeconds(30));
        Assert.All(received, r => Assert.Equal(1, r));
    }
}
