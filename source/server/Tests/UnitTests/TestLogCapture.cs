using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using DOL.GS;
using DOL.GS.PropertyCalc;
using DOL.Logging;

namespace DOL.UnitTests
{
    /// <summary>
    /// Gives the static loggers of some server types a queue the test can read (the server's own log thread is not
    /// started in unit tests, so their lines would otherwise throw), and puts the previous queues back on dispose.
    /// The pattern of UT_BotAttributeProgression, shared.
    /// </summary>
    internal sealed class TestLogCapture : IDisposable
    {
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        private readonly List<(Logger Logger, object Previous, BlockingCollection<LogEntry> Entries)> _logs = new();

        public TestLogCapture(params string[] typeNames)
        {
            FieldInfo queueField = typeof(Logger).GetField("_queueProcessor", Hidden);
            foreach (string name in typeNames)
            {
                Type type = typeof(GameBot).Assembly.GetTypes().FirstOrDefault(t => t.Name == name);
                var logger = (Logger)type?.GetFields(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)
                    .FirstOrDefault(f => f.FieldType == typeof(Logger))?.GetValue(null);
                if (logger == null)
                    continue;
                object previous = queueField.GetValue(logger);
                var queue = new LogEntryQueueProcessor();
                var entries = new BlockingCollection<LogEntry>();
                typeof(LogEntryQueueProcessor).GetField("_loggingQueue", Hidden).SetValue(queue, entries);
                queueField.SetValue(logger, queue);
                _logs.Add((logger, previous, entries));
            }
        }

        /// <summary>Every line logged so far, in order per logger.</summary>
        public IReadOnlyList<string> Lines => _logs.SelectMany(log => log.Entries.Select(entry => entry.Message ?? string.Empty)).ToArray();

        public void Dispose()
        {
            FieldInfo queueField = typeof(Logger).GetField("_queueProcessor", Hidden);
            foreach (var log in _logs)
                queueField.SetValue(log.Logger, log.Previous);
            _logs.Clear();
        }
    }

    /// <summary>Loads the property calculators a constructed GameBot reads (MaxHealth...), and puts the previous ones back
    /// on dispose. The pattern of UT_BotAttributeProgression, shared.</summary>
    internal sealed class TestPropertyCalculators : IDisposable
    {
        private readonly IPropertyCalculator[] _calculators =
            (IPropertyCalculator[])typeof(GameLiving).GetField("m_propertyCalc", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
        private readonly IPropertyCalculator[] _original;

        public TestPropertyCalculators()
        {
            _original = _calculators.ToArray();
            GameLiving.LoadCalculators();
        }

        public void Dispose() => Array.Copy(_original, _calculators, _calculators.Length);
    }
}
