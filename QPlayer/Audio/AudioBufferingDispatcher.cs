using QPlayer.ViewModels;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace QPlayer.Audio;

public class AudioBufferingDispatcher
{
    private readonly Thread[] threadPool;
    private readonly Dictionary<QAudioFileReader, int> audioFiles;
    private readonly Lock lockObj;
    private readonly ConcurrentQueue<WorkItem> lowPriorityWork;
    private readonly ConcurrentQueue<WorkItem> highPriorityWork;
    private readonly ConcurrentDictionary<QAudioFileReader, int> queuedWork;
    private readonly string[] activeWorkDebug;
    private static AudioBufferingDispatcher? defaultInstance;

    public static AudioBufferingDispatcher Default => defaultInstance ??= new();

    /// <summary>
    /// An array of strings representing the current state of each worker thread.
    /// </summary>
    public string[] ActiveWorkDebug => activeWorkDebug;

    public AudioBufferingDispatcher()
    {
        audioFiles = [];
        lowPriorityWork = [];
        highPriorityWork = [];
        queuedWork = [];
        threadPool = new Thread[Math.Max(1, Environment.ProcessorCount - 1)];
        activeWorkDebug = new string[threadPool.Length];
        for (int i = 0; i < threadPool.Length; i++)
        {
            Thread t = new(WorkerLoop);
            t.IsBackground = true;
            t.Priority = ThreadPriority.AboveNormal;
            t.Name = $"Audio Reader {i}";
            t.Start(i);
            threadPool[i] = t;
            activeWorkDebug[i] = "waiting";
        }
        lockObj = new();
        Task.Run(DispatcherLoop);
    }

    public void RegisterAudioFile(QAudioFileReader audioFile)
    {
        lock (lockObj)
        {
            if (!audioFiles.TryAdd(audioFile, 1))
            {
                audioFiles[audioFile]++;
            }
        }
    }

    public void UnregisterAudioFile(QAudioFileReader audioFile)
    {
        lock (lockObj)
        {
            if (audioFiles.TryGetValue(audioFile, out var refs))
            {
                if (refs == 0)
                    audioFiles.Remove(audioFile);
                else
                    audioFiles[audioFile]--;
            }
        }
    }

    private void DispatcherLoop()
    {
        while (true)
        {
            try
            {
                while (true)
                {
                    lock (lockObj)
                    {
                        foreach (var audioFile in audioFiles.Keys)
                        {
                            if (queuedWork.ContainsKey(audioFile))
                                continue;
                            if (audioFile.NeedsStartFilling)
                            {
                                lowPriorityWork.Enqueue(new() { reader = audioFile, fillStart = true });
                                queuedWork.TryAdd(audioFile, 0);
                            }
                            else if (audioFile.NeedsFilling)
                            {
                                if (audioFile.SamplesRemaining < 10000)
                                    highPriorityWork.Enqueue(new() { reader = audioFile });
                                else
                                    lowPriorityWork.Enqueue(new() { reader = audioFile });
                                queuedWork.TryAdd(audioFile, 0);
                            }
                        }
                    }

                    Thread.Sleep(20);
                }
            }
            catch (Exception ex)
            {
                MainViewModel.Log(ex.Message, MainViewModel.LogLevel.Error);
            }
        }
    }

    private void WorkerLoop(object? ind)
    {
        int _ind = (ind as int?) ?? 0;
        while (true)
        {
            try
            {
                while (true)
                {
                    // Try to get a high-priority work item if there is one, failing this, a low priority item.
                    WorkItem work;
                    if (highPriorityWork.TryDequeue(out work))
                    {
                        DoWork(work, _ind);
                        continue;
                    }
                    else if (lowPriorityWork.TryDequeue(out work))
                    {
                        DoWork(work, _ind);
                        continue;
                    }
                    else
                    {
                        // Optimistically try again to get a high-priority item before sleeping.
                        for (int i = 0; i < 4; i++)
                        {
                            Thread.Yield();
                            if (highPriorityWork.TryDequeue(out work))
                            {
                                DoWork(work, _ind);
                                break;
                            }
                        }
                    }

                    Thread.Sleep(30 + 7 * _ind);
                }
            }
            catch (Exception ex)
            {
                MainViewModel.Log(ex.Message, MainViewModel.LogLevel.Error);
            }
        }
    }

    private void DoWork(WorkItem work, int ind)
    {
        var audio = work.reader;
        activeWorkDebug[ind] = $"file: {audio.FileName} // pos: {audio.SamplePosition}";
        //if (audio.SamplePosition == 0)
        //    Debugger.Break();

        try
        {
            if (work.fillStart)
                audio.FillStartBuffer();
            else
                audio.FillBuffer();
        }
        finally
        {
            queuedWork.TryRemove(audio, out _);
            activeWorkDebug[ind] = "waiting";
        }
    }

    private struct WorkItem
    {
        public QAudioFileReader reader;
        public bool fillStart;
    }
}
