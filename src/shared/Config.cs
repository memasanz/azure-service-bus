// Shared helper loaded by notebooks via:  #!import ../src/shared/Config.cs
// Provides a single place to read the connection string + entity names
// so that individual cells stay focused on Service Bus concepts.

using System;

public static class Config
{
    public const string QueueName        = "demo-queue";
    public const string SessionQueueName = "demo-sessions";
    public const string DlqDemoQueueName = "demo-dlq";
    public const string TopicName        = "demo-topic";

    public static string ConnectionString =>
        Environment.GetEnvironmentVariable("SERVICEBUS_CONNECTION_STRING")
            ?? throw new InvalidOperationException(
                "Set the SERVICEBUS_CONNECTION_STRING environment variable before running the notebooks. " +
                "See infra/deploy.ps1 or the README for instructions.");

    public static string FullyQualifiedNamespace =>
        Environment.GetEnvironmentVariable("SERVICEBUS_NAMESPACE")
            ?? throw new InvalidOperationException(
                "Set the SERVICEBUS_NAMESPACE environment variable (e.g. 'sbdemo123.servicebus.windows.net').");
}
