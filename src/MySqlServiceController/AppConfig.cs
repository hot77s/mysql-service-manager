using System.Runtime.Serialization;

namespace MySqlServiceController;

[DataContract]
internal sealed class AppConfig
{
	[DataMember(Order = 1)]
	public string ServiceName { get; set; }

	[DataMember(Order = 2)]
	public string MySqlPath { get; set; }

	[DataMember(Order = 3)]
	public int RefreshSeconds { get; set; }

	[DataMember(Order = 4)]
	public string WorkbenchPath { get; set; }

	[DataMember(Order = 5)]
	public string ThemeMode { get; set; }

	[DataMember(Order = 6)]
	public string WorkbenchLatestVersion { get; set; }

	[DataMember(Order = 7)]
	public string WorkbenchCheckedUtc { get; set; }

	public AppConfig()
	{
		ServiceName = string.Empty;
		MySqlPath = string.Empty;
		RefreshSeconds = 2;
		WorkbenchPath = string.Empty;
		ThemeMode = "system";
		WorkbenchLatestVersion = string.Empty;
		WorkbenchCheckedUtc = string.Empty;
	}
}
