using System.Runtime.Serialization;

namespace PackOPdater.Data
{
	[DataContract]
	public class Mod
	{
		[DataMember(Name = "name")]
		public string Name { get; set; }

		[DataMember(Name = "version")]
		public string Version { get; set; }

		[DataMember(Name = "url")]
		public string URL { get; set; }

		[DataMember(Name = "server")]
		public bool Server { get; set; }

		[DataMember(Name = "client")]
		public bool Client { get; set; }

		[DataMember(Name = "optional")]
		public bool Optional { get; set; }

		public bool Exists { get; set; }
		public bool Enabled { get; set; }
		public string TempFile { get; set; }

		public string FileName { get { return string.Format("{0}-{1}.jar", Name, Version); } }

		public string CurrentFileName { get { return (Enabled ? FileName : (FileName + ".disabled")); } }
	}
}

