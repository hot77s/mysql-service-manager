using System.ServiceProcess;

namespace MySqlServiceController;

internal sealed class ServiceSnapshot
{
	public bool Installed { get; set; }

	public string ServiceName { get; set; }

	public string DisplayName { get; set; }

	public ServiceControllerStatus? Status { get; set; }

	public string StatusText { get; set; }

	public string StartupType { get; set; }

	public string ImagePath { get; set; }

	public string Account { get; set; }

	public bool IsDisabled { get; set; }

	public bool CanStop { get; set; }

	public bool CanStart
	{
		get
		{
			if (Installed && !IsDisabled && Status.HasValue)
			{
				return Status.Value == ServiceControllerStatus.Stopped;
			}
			return false;
		}
	}

	public bool IsRunning
	{
		get
		{
			if (Status.HasValue)
			{
				if (Status.Value != ServiceControllerStatus.Running)
				{
					return Status.Value == ServiceControllerStatus.Paused;
				}
				return true;
			}
			return false;
		}
	}

	public bool IsTransitioning
	{
		get
		{
			if (Status.HasValue)
			{
				if (Status.Value != ServiceControllerStatus.StartPending && Status.Value != ServiceControllerStatus.StopPending && Status.Value != ServiceControllerStatus.PausePending)
				{
					return Status.Value == ServiceControllerStatus.ContinuePending;
				}
				return true;
			}
			return false;
		}
	}

	public string ErrorMessage { get; set; }
}
