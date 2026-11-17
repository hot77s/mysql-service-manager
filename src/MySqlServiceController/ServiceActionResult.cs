namespace MySqlServiceController;

internal sealed class ServiceActionResult
{
	public bool Success { get; set; }

	public string Message { get; set; }

	public string Detail { get; set; }

	public bool WasCancelled { get; set; }
}
