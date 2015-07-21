using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace PackOPdater.Data
{
	[DataContract]
	public class ModpackInfo
	{
		static readonly DataContractJsonSerializer _serializer = new DataContractJsonSerializer(typeof(ModpackInfo));

		public static readonly string FileName = "modpack.json";

		[DataMember(Name = "name")]
		public string Name { get; set; }

		[DataMember(Name = "version")]
		public string Version { get; set; }

		[DataMember(Name = "authors")]
		public List<string> Authors { get; set; }

		[DataMember(Name = "mods")]
		public List<Mod> Mods { get; set; }

		public static ModpackInfo Load(string directory)
		{
			var file = Path.Combine(directory, FileName);
			if (!File.Exists(file)) return null;
			return Parse(File.OpenRead(file));
		}
		public static ModpackInfo Parse(string str)
		{
			return Parse(new MemoryStream(Encoding.UTF8.GetBytes(str)));
		}
		static ModpackInfo Parse(Stream stream)
		{
			using (stream) {
				var info = (ModpackInfo)_serializer.ReadObject(stream);
				info.Mods = info.Mods.OrderBy(mod => mod.Name).ToList();
				return info;
			}
		}

		public void Detect(string directory)
		{
			foreach (var mod in Mods) {
				var file = Path.Combine(directory, "mods", mod.FileName);
				mod.Enabled = File.Exists(file);
				mod.Exists = mod.Enabled || File.Exists(file + ".disabled");
			}
		}

		public IEnumerable<Tuple<Mod, Mod>> Compare(ModpackInfo other)
		{
			var otherMods = ((other != null) ? other.Mods : Enumerable.Empty<Mod>());
			var modNames = Mods.Select(m => m.Name).Union(otherMods.Select(m => m.Name));
			return modNames.OrderBy(n => n).Select(n => Tuple.Create(
				Mods.FirstOrDefault(m => (n == m.Name)),
				otherMods.FirstOrDefault(m => (n == m.Name))));
		}
	}
}

