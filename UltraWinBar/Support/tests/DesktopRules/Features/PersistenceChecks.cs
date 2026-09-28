using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using ManagedShell.AppBar;
using UltraWinBar.Utilities;

internal static class PersistenceChecks
{
    internal static void Run(string[] args, System.IO.DirectoryInfo repositoryRoot)
    {
        string persistenceDirectory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "UltraWinBar-settings-" + Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(persistenceDirectory);
        try
        {
            string settingsPath = System.IO.Path.Combine(persistenceDirectory, "settings.json");
            var manager = new SettingsManager<PersistenceFixture>(settingsPath, new PersistenceFixture { Value = "default" });
            manager.Settings = new PersistenceFixture { Value = "intermediate" };
            manager.Settings = new PersistenceFixture { Value = "saved" };
            manager.Flush();
            if (JsonSerializer.Deserialize<PersistenceFixture>(System.IO.File.ReadAllText(settingsPath))?.Value != "saved")
                throw new Exception("Coalesced settings save did not persist the newest value.");

            System.IO.File.WriteAllText(settingsPath, "{ invalid json");
            var recovered = new SettingsManager<PersistenceFixture>(settingsPath, new PersistenceFixture { Value = "fallback" });
            if (recovered.Settings.Value != "fallback") throw new Exception("Corrupt settings did not fall back to defaults.");
            recovered.Settings = new PersistenceFixture { Value = "recovered" };
            recovered.Flush();
            if (JsonSerializer.Deserialize<PersistenceFixture>(System.IO.File.ReadAllText(settingsPath))?.Value != "recovered" ||
                !System.IO.Directory.EnumerateFiles(persistenceDirectory, "settings.json.corrupt-*").Any(path => System.IO.File.ReadAllText(path) == "{ invalid json"))
                throw new Exception("Corrupt settings backup or atomic recovery save failed.");
        }
        finally
        {
            System.IO.Directory.Delete(persistenceDirectory, true);
        }
        Console.WriteLine("PASS: settings coalesce writes, flush latest values, fall back safely, and preserve corrupt files.");

        string debounceDirectory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "UltraWinBar-settings-debounce-" + Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(debounceDirectory);
        try
        {
            var managerType = typeof(SettingsManager<PersistenceFixture>);
            var serializeCountField = managerType.GetField("SerializeCount", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var saveTimerElapsedMethod = managerType.GetMethod("SaveTimerElapsed", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            long SerializeCountOf(object instance) => (long)serializeCountField.GetValue(instance);

            // A burst of changes must serialize once (on flush), not once per change.
            string burstPath = System.IO.Path.Combine(debounceDirectory, "burst.json");
            var burstManager = new SettingsManager<PersistenceFixture>(burstPath, new PersistenceFixture { Value = "default" });
            for (int i = 0; i < 5; i++)
                burstManager.Settings = new PersistenceFixture { Value = $"v{i}" };
            if (SerializeCountOf(burstManager) != 0)
                throw new Exception($"A burst of settings changes must not serialize before flush/debounce; serialized {SerializeCountOf(burstManager)} times.");
            burstManager.Flush();
            if (SerializeCountOf(burstManager) != 1)
                throw new Exception($"A burst of settings changes must serialize exactly once on flush; serialized {SerializeCountOf(burstManager)} times.");
            if (JsonSerializer.Deserialize<PersistenceFixture>(System.IO.File.ReadAllText(burstPath))?.Value != "v4")
                throw new Exception("Flush after a burst did not persist the latest value.");

            // Flush must persist the latest value synchronously even over a pending debounce.
            burstManager.Settings = new PersistenceFixture { Value = "pending" };
            burstManager.Flush();
            if (SerializeCountOf(burstManager) != 2)
                throw new Exception("Flush over a pending debounce should serialize exactly once more.");
            if (JsonSerializer.Deserialize<PersistenceFixture>(System.IO.File.ReadAllText(burstPath))?.Value != "pending")
                throw new Exception("Flush did not persist the latest value over a pending debounce.");

            var previousContext = System.Threading.SynchronizationContext.Current;
            try
            {
                // No captured SynchronizationContext (console/tests): debounce elapsing must
                // serialize inline rather than posting anywhere.
                System.Threading.SynchronizationContext.SetSynchronizationContext(null);
                string fallbackPath = System.IO.Path.Combine(debounceDirectory, "fallback.json");
                var fallbackManager = new SettingsManager<PersistenceFixture>(fallbackPath, new PersistenceFixture { Value = "default" });
                fallbackManager.Settings = new PersistenceFixture { Value = "fallback-value" };
                if (SerializeCountOf(fallbackManager) != 0)
                    throw new Exception("A settings change must not serialize before the debounce fires, even with no captured context.");
                saveTimerElapsedMethod.Invoke(fallbackManager, new object[] { null });
                if (SerializeCountOf(fallbackManager) != 1)
                    throw new Exception("Debounce elapsing with no captured SynchronizationContext must serialize inline.");
                fallbackManager.Flush();
                if (JsonSerializer.Deserialize<PersistenceFixture>(System.IO.File.ReadAllText(fallbackPath))?.Value != "fallback-value")
                    throw new Exception("No-context fallback did not persist the debounced value.");

                // Settings is constructed before the UI dispatcher runs; the first change made under a
                // context must adopt it, and serialization must be posted there, not run inline.
                var recordingContext = new RecordingSyncContext();
                string postedPath = System.IO.Path.Combine(debounceDirectory, "posted.json");
                var postedManager = new SettingsManager<PersistenceFixture>(postedPath, new PersistenceFixture { Value = "default" });
                System.Threading.SynchronizationContext.SetSynchronizationContext(recordingContext);
                postedManager.Settings = new PersistenceFixture { Value = "posted-value" };
                System.Threading.SynchronizationContext.SetSynchronizationContext(null);
                saveTimerElapsedMethod.Invoke(postedManager, new object[] { null });
                if (recordingContext.PostCount != 1 || SerializeCountOf(postedManager) != 0)
                    throw new Exception("Debounce elapsing must post serialization to the owner context adopted from the first change, not run inline.");
                recordingContext.Pump();
                if (SerializeCountOf(postedManager) != 1)
                    throw new Exception("Posting to the owner context did not serialize the settings.");
                postedManager.Flush();
                if (JsonSerializer.Deserialize<PersistenceFixture>(System.IO.File.ReadAllText(postedPath))?.Value != "posted-value")
                    throw new Exception("Serialization posted to the owner context did not persist the latest value.");
            }
            finally
            {
                System.Threading.SynchronizationContext.SetSynchronizationContext(previousContext);
            }
        }
        finally
        {
            System.IO.Directory.Delete(debounceDirectory, true);
        }
        Console.WriteLine("PASS: settings changes debounce serialization to once per burst, flush forces it inline, and the no-context/posted-context paths each serialize exactly once.");

        var loggerType = typeof(TaskAssignmentManager).Assembly.GetType("UltraWinBar.Utilities.ManagedShellLogger");
        var retentionField = loggerType.GetField("LogRetention", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        if ((TimeSpan)retentionField.GetValue(null) != TimeSpan.FromDays(7))
            throw new Exception("Log retention must be 7 days.");

        var rollingLogType = typeof(TaskAssignmentManager).Assembly.GetType("UltraWinBar.Utilities.RollingFileLog");
        var shouldRollMethod = rollingLogType.GetMethod("ShouldRoll", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        var buildNameMethod = rollingLogType.GetMethod("BuildLogFileName", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        bool RollAt(long size, long cap) => (bool)shouldRollMethod.Invoke(null, new object[] { size, cap });
        if (RollAt(19 * 1024 * 1024, 20 * 1024 * 1024) || !RollAt(20 * 1024 * 1024, 20 * 1024 * 1024) || !RollAt(21 * 1024 * 1024, 20 * 1024 * 1024))
            throw new Exception("Roll-over decision must trigger once size reaches the cap, not before.");
        var rollTimestamp = new DateTime(2026, 1, 2, 3, 4, 5, 6);
        string firstRollName = (string)buildNameMethod.Invoke(null, new object[] { rollTimestamp, 0 });
        string secondRollName = (string)buildNameMethod.Invoke(null, new object[] { rollTimestamp, 1 });
        if (firstRollName == secondRollName)
            throw new Exception("Successive rolls within the same process must produce unique file names even with identical timestamps.");
        Console.WriteLine("PASS: log retention is 7 days and the roll-over decision and file naming are deterministic and collision-free.");

        var settingsType = typeof(TaskAssignmentManager).Assembly.GetType("UltraWinBar.Utilities.Settings");
        var settings = Activator.CreateInstance(settingsType);
        var actualDefaults = System.Text.Json.Nodes.JsonNode.Parse(JsonSerializer.Serialize(settings, settingsType));
        var expectedDefaults = System.Text.Json.Nodes.JsonNode.Parse(System.IO.File.ReadAllText(
            System.IO.Path.Combine(AppContext.BaseDirectory, "UltraWinBar.defaults.json"))).AsObject();
        foreach (var property in expectedDefaults)
            if (!System.Text.Json.Nodes.JsonNode.DeepEquals(property.Value, actualDefaults[property.Key]))
                throw new Exception($"Default mismatch: {property.Key}: expected {property.Value}, actual {actualDefaults[property.Key]}");
        foreach (var name in new[] { "PinnedApplications", "TaskOrder", "TaskbarAssignments", "AllDesktopApplications" })
            if (actualDefaults[name].AsArray().Count != 0) throw new Exception($"Personal data in defaults: {name}");
        if (actualDefaults["MoveActivatedWindowsToCurrentDesktop"].GetValue<bool>())
            throw new Exception("Experimental desktop activation must be disabled by default.");
        Console.WriteLine("PASS: current general settings are defaults; no personal pins, desktop IDs or assignments embedded.");
    }
}
