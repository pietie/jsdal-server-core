using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using Serilog;

namespace jsdal_server_core.Util
{
    public class QueueThread<T>
    {
        private ConcurrentQueue<T> _queue;
        private Thread _winThread;
        protected bool IsRunning;

        private int _flushTimeoutInSeconds;
        private int _flushCountThreshold;

        public QueueThread(int flushTimeoutInSeconds = 25, int flushCountThreshold = 300)
        {
            this._flushTimeoutInSeconds = flushTimeoutInSeconds;
            this._flushCountThreshold = flushCountThreshold;
        }
        public void Init()
        {
            _queue = new ConcurrentQueue<T>();
            _winThread = new Thread(new ThreadStart(ProcessMessagesLoop));
            _winThread.Start();
        }

        protected virtual void ProcessQueueEntries(List<T> entryCollection)
        {
            throw new NotImplementedException();
        }

        protected virtual void DoWork()
        {
        }

        public void Enqueue(T entry)
        {
            _queue.Enqueue(entry);
        }

        private void ProcessMessagesLoop()
        {
            try
            {
                IsRunning = true;

                var nextFlush = DateTime.Now.AddSeconds(_flushTimeoutInSeconds);

                while (IsRunning && !Program.IsShuttingDown)
                {
                    // timeout or count trigger check 
                    if (DateTime.Now >= nextFlush || _queue.Count >= _flushCountThreshold)
                    {
                        ProcessQueueUntilEmpty();

                        nextFlush = DateTime.Now.AddSeconds(_flushTimeoutInSeconds);
                    }

                    // perform any additional work that might be required
                    DoWork();

                    Thread.Sleep(60);
                }

                // flush any remaining items out
                ProcessQueueUntilEmpty();
                DoFinalWork();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "ProcessMessagesLoop failed");
                ExceptionLogger.LogException(ex);
                SessionLog.Error("ProcessMessagesLoop failed");
                SessionLog.Exception(ex);
            }
            finally
            {
                IsRunning = false;
            }
        }

        protected void ProcessQueueUntilEmpty()
        {
            List<T> entryCollection = null;

            lock (_queue)
            {
                if (_queue.IsEmpty) return;

                entryCollection = new List<T>();

                while (!_queue.IsEmpty)
                {
                    if (_queue.TryDequeue(out var entry))
                    {
                        entryCollection.Add(entry);
                    }
                }
            }

            try
            {
                if (entryCollection != null)
                {
                    ProcessQueueEntries(entryCollection);
                }
            }
            catch (Exception ee)
            {
                HandleProcessException(ee);
            }
        }

        protected virtual void HandleProcessException(Exception ex)
        {

        }

        protected virtual void DoFinalWork()
        {

        }

        public void Shutdown()
        {
            IsRunning = false;
            if (_winThread != null)
            {
                if (!_winThread.Join(TimeSpan.FromSeconds(15)))
                {
                    Log.Error("ExceptionsDB failed to shutdown in time");
                }
                _winThread = null;
            }
        }
    }


}