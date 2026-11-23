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

	/// <summary>最近一次自更新检查得到的 MySQL管家 最新版本。</summary>
	[DataMember(Order = 8)]
	public string AppLatestVersion { get; set; }

	/// <summary>最近一次自更新检查时间（UTC，往返格式）。</summary>
	[DataMember(Order = 9)]
	public string AppCheckedUtc { get; set; }

	public AppConfig()
	{
		ServiceName = string.Empty;
		MySqlPath = string.Empty;
		RefreshSeconds = 2;
		WorkbenchPath = string.Empty;
		ThemeMode = "system";
		WorkbenchLatestVersion = string.Empty;
		WorkbenchCheckedUtc = string.Empty;
		AppLatestVersion = string.Empty;
		AppCheckedUtc = string.Empty;
	}
}
