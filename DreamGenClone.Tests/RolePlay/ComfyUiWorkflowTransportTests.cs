using System.Text.Json.Nodes;
using DreamGenClone.Infrastructure.Models;

namespace DreamGenClone.Tests.RolePlay;

/// <summary>
/// The history entry is the ONLY place ComfyUI explains why a prompt failed, so these tests pin the
/// detail that the exception message carries back to the UI.
/// </summary>
public sealed class ComfyUiWorkflowTransportTests
{
    [Fact]
    public void DescribeExecutionError_ReportsNodeExceptionAndCollapsedMessage()
    {
        var entry = new JsonObject
        {
            ["status"] = new JsonObject
            {
                ["status_str"] = "error",
                ["messages"] = new JsonArray
                {
                    new JsonArray("execution_start", new JsonObject { ["prompt_id"] = "abc" }),
                    new JsonArray(
                        "execution_error",
                        new JsonObject
                        {
                            ["node_id"] = "1",
                            ["node_type"] = "LoadImage",
                            ["exception_type"] = "torch.AcceleratorError",
                            ["exception_message"] = "CUDA error: unknown error\nSticky error detected\n",
                        }),
                },
            },
        };

        var detail = ComfyUiWorkflowTransport.DescribeExecutionError(entry);

        Assert.Equal(
            " — LoadImage (node 1) — torch.AcceleratorError — CUDA error: unknown error Sticky error detected",
            detail);
    }

    [Fact]
    public void DescribeExecutionError_ReturnsEmptyWhenComfyUiReportedNoExecutionError()
    {
        var entry = new JsonObject
        {
            ["status"] = new JsonObject
            {
                ["status_str"] = "success",
                ["messages"] = new JsonArray
                {
                    new JsonArray("execution_start", new JsonObject()),
                    new JsonArray("execution_success", new JsonObject()),
                },
            },
        };

        Assert.Equal(string.Empty, ComfyUiWorkflowTransport.DescribeExecutionError(entry));
    }

    [Fact]
    public void DescribeExecutionError_ReturnsEmptyWhenStatusHasNoMessages()
    {
        var entry = new JsonObject { ["status"] = new JsonObject { ["status_str"] = "error" } };

        Assert.Equal(string.Empty, ComfyUiWorkflowTransport.DescribeExecutionError(entry));
    }

    [Fact]
    public void DescribeExecutionError_TruncatesVeryLongMessages()
    {
        var entry = new JsonObject
        {
            ["status"] = new JsonObject
            {
                ["status_str"] = "error",
                ["messages"] = new JsonArray
                {
                    new JsonArray(
                        "execution_error",
                        new JsonObject
                        {
                            ["node_id"] = "9",
                            ["node_type"] = "KSampler",
                            ["exception_message"] = new string('x', 5000),
                        }),
                },
            },
        };

        var detail = ComfyUiWorkflowTransport.DescribeExecutionError(entry);

        Assert.EndsWith("…", detail, StringComparison.Ordinal);
        Assert.True(detail.Length < 700, $"detail length was {detail.Length}");
    }
}
