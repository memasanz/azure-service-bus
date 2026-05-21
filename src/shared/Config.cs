// Shared helper loaded by notebooks via:  #!import ../src/shared/Config.cs
//
// Reads configuration from a `.env` file in the repo root (or any parent
// directory of the notebook), falling back to real process environment
// variables. This keeps notebooks free of secrets while giving you a simple
// place to drop a connection string.
//
// .env format:
//   KEY=VALUE
//   # comments and blank lines are ignored
//   QUOTED="values with = signs are fine"

using System;
using System.Collections.Generic;
using System.IO;

public static class Config
{
    public const string QueueName        = "demo-queue";
    public const string SessionQueueName = "demo-sessions";
    public const string DlqDemoQueueName = "demo-dlq";
    public const string TopicName        = "demo-topic";

    private static readonly Dictionary<string, string> _values = LoadDotEnv();

    public static string ConnectionString =>
        Get("SERVICEBUS_CONNECTION_STRING")
            ?? throw new InvalidOperationException(
                "SERVICEBUS_CONNECTION_STRING is not set. " +
                "Create a `.env` file in the repo root (see `.env.example`) " +
                "or set the environment variable. " +
                "Tip: `infra/deploy.ps1` writes the .env for you.");

    public static string FullyQualifiedNamespace =>
        Get("SERVICEBUS_NAMESPACE")
            ?? throw new InvalidOperationException(
                "SERVICEBUS_NAMESPACE is not set (expected e.g. 'sbdemo123.servicebus.windows.net'). " +
                "Add it to your `.env` file or set the environment variable.");

    /// <summary>Get a value, preferring .env, then real process env vars.</summary>
    public static string? Get(string key)
    {
        if (_values.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v))
            return v;
        var env = Environment.GetEnvironmentVariable(key);
        return string.IsNullOrWhiteSpace(env) ? null : env;
    }

    /// <summary>Path of the .env file we loaded (or null if none was found).</summary>
    public static string? DotEnvPath { get; private set; }

    private static Dictionary<string, string> LoadDotEnv()
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // Walk up from CWD looking for a .env file.
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, ".env");
            if (File.Exists(candidate))
            {
                DotEnvPath = candidate;
                foreach (var raw in File.ReadAllLines(candidate))
                {
                    var line = raw.Trim();
                    if (line.Length == 0 || line.StartsWith("#")) continue;

                    var eq = line.IndexOf('=');
                    if (eq <= 0) continue;

                    var key = line.Substring(0, eq).Trim();
                    var val = line.Substring(eq + 1).Trim();

                    if (val.Length >= 2 &&
                        ((val.StartsWith("\"") && val.EndsWith("\"")) ||
                         (val.StartsWith("'")  && val.EndsWith("'"))))
                    {
                        val = val.Substring(1, val.Length - 2);
                    }

                    result[key] = val;
                }
                break;
            }
            dir = dir.Parent;
        }

        return result;
    }
}
