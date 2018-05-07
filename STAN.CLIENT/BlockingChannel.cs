﻿/*******************************************************************************
 * Copyright (c) 2015-2016 Apcera Inc. All rights reserved. This program and the accompanying
 * materials are made available under the terms of the MIT License (MIT) which accompanies this
 * distribution, and is available at http://opensource.org/licenses/MIT
 *******************************************************************************/
using System;
using System.Collections.Generic;
using System.Threading;

namespace STAN.Client
{

    // This dictionary class is a capacity bound, threadsafe, dictionary.
    internal sealed class BlockingDictionary<TKey, TValue>
    {
        IDictionary<TKey, TValue> d = new Dictionary<TKey, TValue>();
        Object dLock   = new Object();
        Object addLock = new Object();
        bool finished = false;
        long maxSize = 1024;

        private bool isAtCapacity()
        {
            lock (dLock)
            {
                return d.Count >= maxSize;
            }
        }

        internal void waitForSpace()
        {
            try
            {
                Serilog.Log.Debug("STAN waitForSpace");
                lock (addLock)
                {
                    while (isAtCapacity())
                    {
                        Monitor.Wait(addLock);
                    }
                }
            }
            finally
            {
                Serilog.Log.Debug("STAN waitForSpace DONE");
            }
        }

        private BlockingDictionary() { }

        internal BlockingDictionary(long maxSize)
        {
            if (maxSize <= 0)
                throw new ArgumentOutOfRangeException("maxSize", maxSize, "maxSize must be greater than 0");

            this.maxSize = maxSize;
        }

        internal bool Remove(TKey key, out TValue value, int timeout, string source)
        {
            try
            {
                Serilog.Log.Debug("STAN Remove {source}", source);
                bool rv = false;
                bool wasAtCapacity = false;

                value = default(TValue);

                lock (dLock)
                {
                    if (!finished && timeout != 0)
                    {
                        // check and wait if empty
                        while (d.Count == 0)
                        {
                            if (timeout < 0)
                            {
                                Monitor.Wait(dLock);
                            }
                            else
                            {
                                if (timeout > 0)
                                {
                                    if (Monitor.Wait(dLock, timeout) == false)
                                    {
                                        throw new Exception("timeout");
                                    }
                                }
                            }
                        }

                        if (!finished)
                        {
                            rv = d.TryGetValue(key, out value);
                        }
                    }

                    if (rv)
                    {
                        wasAtCapacity = d.Count >= maxSize;
                        d.Remove(key);

                        if (wasAtCapacity)
                        {
                            lock (addLock)
                            {
                                Monitor.Pulse(addLock);
                            }
                        }
                    }
                }

                return rv;
            }
            finally
            {
                Serilog.Log.Debug("STAN Remove DONE");
            }

        } // get

        // if false, caller should waitForSpace then
        // call again (until true)
        internal bool TryAdd(TKey key, TValue value)
        {
            try
            {
                Serilog.Log.Debug("STAN TryAdd");
                lock (dLock)
                {
                    // if at capacity, do not attempt to add
                    if (d.Count >= maxSize)
                    {
                        return false;
                    }

                    d[key] =  value;

                    // if the queue count was previously zero, we were
                    // waiting, so signal.
                    if (d.Count <= 1)
                    {
                        Monitor.Pulse(dLock);
                    }

                    return true;
                }
            }
            finally
            {
                Serilog.Log.Debug("STAN TryAdd DONE");
            }
        }

        internal void close()
        {
            try
            {
                Serilog.Log.Debug("STAN close");
                lock (dLock)
                {
                    finished = true;
                    Monitor.Pulse(dLock);
                }
            }
            finally
            {
                Serilog.Log.Debug("STAN close DONE");
            }
        }

        internal int Count
        {
            get
            {
                try
                {
                    Serilog.Log.Debug("STAN Count");
                    lock (dLock)
                    {
                        return d.Count;
                    }
                }
                finally
                {
                    Serilog.Log.Debug("STAN Count DONE");
                }
            }
        }
    } // class BlockingChannel
}

