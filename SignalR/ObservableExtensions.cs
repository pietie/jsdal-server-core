using System;
//using System.Reactive.Linq;
using System.Threading.Channels;




//https://github.com/aspnet/SignalR/blob/f5e85a5c31b6ac6b283481cf005106aeec59dbae/samples/SignalRSamples/ObservableExtensions.cs
namespace jsdal_server_core
{
    public static class ObservableExtensions
    {
        public static ChannelReader<T> AsChannelReader<T>(this System.IObservable<T> observable)
        {
            // This sample shows adapting an observable to a ChannelReader without 
            // back pressure, if the connection is slower than the producer, memory will
            // start to increase.

            // If the channel is unbounded, TryWrite will return false and effectively
            // drop items.

            // The other alternative is to use a bounded channel, and when the limit is reached
            // block on WaitToWriteAsync. This will block a thread pool thread and isn't recommended
            var channel = Channel.CreateUnbounded<T>();

            var disposable = observable.Subscribe(
                                value => channel.Writer.TryWrite(value),
                                error => channel.Writer.TryComplete(error),
                                () => channel.Writer.TryComplete());

            // Complete the subscription on the reader completing
            channel.Reader.Completion.ContinueWith(task => disposable.Dispose());

            return channel.Reader;
        }
    }
}