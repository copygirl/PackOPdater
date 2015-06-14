using System;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace PackOPdater.Data
{
	[DataContract]
	public class AppSettings
	{
		static readonly DataContractJsonSerializer _serializer =
			new DataContractJsonSerializer(typeof(AppSettings));

		public static readonly string FileName = "Pack[OP]dater.json";

		[DataMember]
		public string Owner { get; set; }

		[DataMember]
		public string Repository { get; set; }

		[DataMember]
		public string Branch { get; set; }

		public static AppSettings Load()
		{
			var file = Path.Combine(Environment.CurrentDirectory, FileName);
			if (File.Exists(file)) {
				using (var fs = File.OpenRead(file))
					return (AppSettings)_serializer.ReadObject(fs);
			}
			return new AppSettings();
		}

		public void Save()
		{
			var file = Path.Combine(Environment.CurrentDirectory, FileName);
			using (var fs = File.OpenWrite(file))
				_serializer.WriteObject(fs, this);
		}
	}
}

