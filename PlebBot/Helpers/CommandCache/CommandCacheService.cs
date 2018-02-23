﻿using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Discord;
using Discord.WebSocket;

namespace PlebBot.Helpers.CommandCache
{
    public class CommandCacheService : ICommandCache<ulong, ConcurrentBag<ulong>>, IDisposable
    {
        public const int UNLIMITED = -1; // POWEEEEEEERRRRRRRR

        private readonly ConcurrentDictionary<ulong, ConcurrentBag<ulong>> _cache
            = new ConcurrentDictionary<ulong, ConcurrentBag<ulong>>();
        private int _max;
        private Timer _autoClear;
        private Func<LogMessage, Task> _logger;
        private int _count;

        public CommandCacheService(DiscordSocketClient client, int capacity = 200, Func<LogMessage, Task> log = null)
        {
            // If a method for logging is supplied, use it, otherwise use a method that does nothing.
            _logger = log ?? (_ => Task.CompletedTask);

            // Make sure the max capacity is within an acceptable range, use it if it is.
            if (capacity < 1 && capacity != UNLIMITED)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity), capacity, "Capacity can not be lower than 1 unless capacity is CommandCacheService.UNLIMITED.");
            }
            else
            {
                _max = capacity;
            }

            // Create a timer that will clear out cached messages older than 2 hours every 30 minutes.
            _autoClear = new Timer(OnTimerFired, null, 1800000, 1800000);

            client.MessageDeleted += OnMessageDeleted;

            _logger(new LogMessage(LogSeverity.Verbose, "CmdCache", $"Service initialised, MessageDeleted event handler registered."));
        }

        public IEnumerable<ulong> Keys => _cache.Keys;

        public IEnumerable<ConcurrentBag<ulong>> Values => _cache.Values;

        public int Count => _count;

        public void Add(ulong key, ConcurrentBag<ulong> values)
        {
            if (values == null)
            {
                throw new ArgumentNullException(nameof(values), "The supplied collection can not be null.");
            }

            if (_max != UNLIMITED && _count >= _max)
            {
                int removeCount = _count - _max + 1;
                // The left 42 bits represent the timestamp.
                var orderedKeys = _cache.Keys.OrderBy(k => k >> 22).ToList();
                // Remove items until we're under the maximum.
                int successfulRemovals = 0;
                foreach (var orderedKey in orderedKeys)
                {
                    if (successfulRemovals >= removeCount) break;

                    var success = Remove(orderedKey);
                    if (success) successfulRemovals++;
                }

                // Reset _count to _cache.Count.
                UpdateCount();
            }

            // TryAdd will return false if the key already exists, in which case we don't want to increment the count.
            if (_cache.TryAdd(key, values))
            {
                Interlocked.Increment(ref _count);
            }
            else
            {
                _cache[key].AddMany(values);
            }
        }

        public void Add(KeyValuePair<ulong, ConcurrentBag<ulong>> pair) => Add(pair.Key, pair.Value);

        public void Add(ulong key, ulong value)
        {
            if (!TryGetValue(key, out ConcurrentBag<ulong> bag))
            {
                Add(key, bag = new ConcurrentBag<ulong>() { value });
            }
            else
            {
                bag.Add(value);
            }
        }

        public void Add(ulong key, params ulong[] values) => Add(key, new ConcurrentBag<ulong>(values));

        public void Add(IUserMessage command, IUserMessage response) => Add(command.Id, response.Id);

        public void Clear()
        {
            _cache.Clear();
            Interlocked.Exchange(ref _count, 0);
        }

        public bool ContainsKey(ulong key) => _cache.ContainsKey(key);

        public IEnumerator<KeyValuePair<ulong, ConcurrentBag<ulong>>> GetEnumerator() => _cache.GetEnumerator();

        public bool Remove(ulong key)
        {
            var success = _cache.TryRemove(key, out ConcurrentBag<ulong> _);
            if (success) Interlocked.Decrement(ref _count);
            return success;
        }

        public bool TryGetValue(ulong key, out ConcurrentBag<ulong> value) => _cache.TryGetValue(key, out value);

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public void Dispose()
        {
            Dispose(true);
        }

        protected void Dispose(bool disposing)
        {
            if (disposing && _autoClear != null)
            {
                _autoClear.Dispose();
                _autoClear = null;
            }
        }

        private void OnTimerFired(object state)
        {
            // Get all messages where the timestamp is older than 2 hours, then convert it to a list. The result of where merely contains references to the original
            // collection, so iterating and removing will throw an exception. Converting it to a list first avoids this.
            var purge = _cache.Where(p =>
            {
                // The timestamp of a message can be calculated by getting the leftmost 42 bits of the ID, then
                // adding January 1, 2015 as a Unix timestamp.
                DateTimeOffset timestamp = DateTimeOffset.FromUnixTimeMilliseconds((long)((p.Key >> 22) + 1420070400000UL));
                TimeSpan difference = DateTimeOffset.UtcNow - timestamp;

                return difference.TotalHours >= 2.0;
            }).ToList();

            var removed = purge.Where(p => Remove(p.Key));

            UpdateCount();

            _logger(new LogMessage(LogSeverity.Verbose, "CmdCache", $"Cleaned {removed.Count()} items from the cache."));
        }

        private async Task OnMessageDeleted(Cacheable<IMessage, ulong> cacheable, ISocketMessageChannel channel)
        {
            if (TryGetValue(cacheable.Id, out ConcurrentBag<ulong> messages))
            {
                foreach (var messageId in messages)
                {
                    var message = await channel.GetMessageAsync(messageId);
                    if (message != null)
                    {
                        await message.DeleteAsync();
                    }
                    else
                    {
                        await _logger(new LogMessage(LogSeverity.Warning, "CmdCache", $"{cacheable.Id} deleted but {messageId} does not exist."));
                    }
                }
                Remove(cacheable.Id);
            }
        }

        private void UpdateCount() => Interlocked.Exchange(ref _count, _cache.Count);
    }
}