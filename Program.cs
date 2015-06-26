using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Octokit;
using PackOPdater.Data;

namespace PackOPdater
{
	class Program
	{
		public string WorkingDir { get; set; }
		public OPdater OPdater { get; set; }

		public string ServerJar { get; set; }
		public ServerWrapper Wrapper { get; set; }
		public string[] Arguments { get; set; }

		public bool IsServer { get { return (ServerJar != null); } }

		static void Main(string[] args)
		{
			new Program(args).Run();
		}

		Program(string[] args)
		{
			WorkingDir = Environment.CurrentDirectory;
			OPdater = new OPdater(WorkingDir);

			ParseArguments(args);
		}

		void ParseArguments(string[] args)
		{
			if (args.Length <= 0) return;
			ServerJar = args[0];
			Arguments = args.Skip(1).ToArray();
		}

		void Run()
		{
			Console.WriteLine("Pack[OP]dater - Easy modpack syncing using GitHub");
			Console.WriteLine("For more information see: http://github.com/copygirl/PackOPdater");
			Console.WriteLine();

			if (OPdater.Settings.Owner == null)
				EnterSettings();

			DisplayModpackInfo();

			if (IsServer) {

				Wrapper = new ServerWrapper(WorkingDir, ServerJar, Arguments);
				Wrapper.Output += Console.WriteLine;

				ServerUpdateCheckerLoop().Wait();

				throw new Exception("Good job, you managed to kill the poor update checker.");
				// TODO: Figure out how to gracefully shut down this thing.

			} else {
				
				if (OPdater.IsUpdateAvailable().Result) {
					Console.Write("> Update available, download now? ");
					if (SelectYesNo())
						DownloadLatestClient().Wait();
				} else
					Console.WriteLine("No update available.");

				Console.WriteLine();
				CheckForMissingMods();
				
			}

			Console.WriteLine("Press any key to continue ...");
			Console.ReadKey();
		}

		void DisplayModpackInfo()
		{
			var info = OPdater.CurrentModpackInfo;
			if (info == null) return;

			Console.WriteLine("Modpack: {0} ({1}) by {2}", info.Name, info.Version, string.Join(", ", info.Authors));
			info.Detect(WorkingDir);

			Console.WriteLine();
		}

		void CheckForMissingMods()
		{
			var info = OPdater.CurrentModpackInfo;
			if (info == null) return;
			info.Detect(WorkingDir);

			var missingMods = new List<Mod>();
			foreach (var mod in info.Mods)
				if (!mod.Optional && !mod.Enabled)
					missingMods.Add(mod);

			if (missingMods.Count > 0) {
				foreach (var mod in missingMods)
					Console.WriteLine("  Warning: Required mod '{0}' is {1}", mod.Name, (mod.Exists ? "disabled" : "missing"));
				Console.WriteLine();

				Console.Write("Download missing mods? ");
				if (SelectYesNo()) {
					var mods = missingMods.Select(mod => Tuple.Create<Mod, Mod>(mod, null)).ToList();
					DownloadMods(mods).Wait();
					UpdateMods(mods, new List<Mod>());
				}
			}

			Console.WriteLine();
		}

		#region Downloading and updating

		async Task DownloadLatestClient()
		{
			Console.Write("Grabbing latest modpack info... ");
			ModpackInfo latest;
			try { latest = await OPdater.GetLatestModpackInfo(); }
			catch { Console.WriteLine("ERROR"); return; }
			Console.WriteLine("DONE");

			var toDownload = new List<Tuple<Mod, Mod>>();
			var toDownloadOptional = new List<Mod>();
			var toDelete = new List<Mod>();
			BuildModUpdateLists(latest, toDownload, toDownloadOptional, toDelete);

			if (toDownloadOptional.Count > 0) {
				Console.WriteLine();
				SelectOptional(toDownloadOptional);
				foreach (var mod in toDownloadOptional)
					if (mod.Enabled)
						toDownload.Add(Tuple.Create(mod, (Mod)null));
				Console.WriteLine();
			}

			if (toDownload.Count > 0) {
				Console.WriteLine("Downloading mods...");
				await DownloadMods(toDownload);
			}

			Console.Write("Updating local git repository... ");
			await OPdater.CloneOrUpdate();
			Console.WriteLine("DONE");

			UpdateMods(toDownload, toDelete);
		}

		async Task DownloadMods(List<Tuple<Mod, Mod>> toDownload)
		{
			await OPdater.Download(toDownload.Select(x => x.Item1), (mod, recv, total) => {
				var index = toDownload.IndexOf(x => (mod == x.Item1));
				var oldMod = toDownload[index].Item2;
				var version = ((oldMod != null) ? string.Format("{0} -> {1}", oldMod.Version, mod.Version) : mod.Version);

				var progress = ((total > 0) ? ((double)recv / total) : 0);
				progress = (index + progress) / toDownload.Count * 100;

				lock (OPdater) {
					Console.Write("{0," + (1 - Console.WindowWidth) + "}",
						string.Format("[{0,3:0}%] {1} ({2}) [{3}/{4} KiB]",
							(int)progress, mod.Name, version,
							recv / 1024, ((total > 0) ? (total / 1024).ToString() : "???")));
					Console.SetCursorPosition(0, Console.CursorTop);
				}
			});

			Console.WriteLine();
		}

		async Task ServerUpdateCheckerLoop(CancellationToken ct = default(CancellationToken))
		{
			while (!ct.IsCancellationRequested) {
				if (await OPdater.IsUpdateAvailable()) {
					ModpackInfo latest = await OPdater.GetLatestModpackInfo();

					var toDownload = new List<Tuple<Mod, Mod>>();
					var toDelete = new List<Mod>();
					BuildModUpdateLists(latest, toDownload, null, toDelete);

					if (Wrapper.Running && (Wrapper.Players.Count > 0)) {
						// Write a nice message to the players telling them about the new update.

						var url = "https://github.com/" + OPdater.Settings.Owner + "/commits/" + OPdater.Settings.Repository;
						if (OPdater.Settings.Branch != "master")
							url += "/tree/" + OPdater.Settings.Branch;

						var parts = new List<string>();
						parts.Add(@"{""text"":""[ UPDATE!! ]"",""color"":""red"",""bold"":""true""}");
						parts.Add(@"{""text"":"" Version " + latest.Version + @""",""color"":""yellow"",""bold"":""false""}");

						var newMods = toDownload.Count(pair => (pair.Item2 == null));
						var changedMods = toDownload.Count(pair => (pair.Item2 != null));

						if (newMods > 0)
							parts.Add(@"{""text"":""" + newMods + @""",""color"":""green"",""bold"":""true""}");
						if (changedMods > 0)
							parts.Add(@"{""text"":""" + changedMods + @""",""color"":""gray"",""bold"":""true""}");
						if (toDelete.Count > 0)
							parts.Add(@"{""text"":""" + toDelete.Count + @""",""color"":""red"",""bold"":""true""}");

						parts.Add(@"{""text"":"" ("",""color"":""yellow"",""bold"":""false""},{""text"":""View Online"",""color"":""aqua"",""underlined"":""true"",""clickEvent"":{""action"":""open_url"",""value"":""" + url + @"""}},{""text"":"")"",""color"":""yellow"",""underlined"":""false""}");

						Wrapper.Input("/tellraw @p [" + string.Join(@","" "",", parts) + "]");
					}

					if (toDownload.Count > 0)
						await OPdater.Download(toDownload.Select(x => x.Item1), null);

					if (Wrapper.Running) {
						Wrapper.AutoRestart = false;
						await Wrapper.Stop();
					}

					await OPdater.CloneOrUpdate();

					UpdateMods(toDownload, toDelete);
				}

				if (!Wrapper.Running) {
					Wrapper.AutoRestart = true;
					Wrapper.Start();
				}

				await Task.Delay(TimeSpan.FromMinutes(2), ct);
			}

			if (Wrapper.Running)
				await Wrapper.Stop();
		}


		void BuildModUpdateLists(ModpackInfo latest, List<Tuple<Mod, Mod>> toDownload, List<Mod> toDownloadOptional, List<Mod> toDelete)
		{
			var compare = latest.Compare(OPdater.CurrentModpackInfo);
			foreach (var newOldPair in compare) {
				var newMod = newOldPair.Item1;
				var oldMod = newOldPair.Item2;
				if ((newMod != null) && (IsServer ? newMod.Server : newMod.Client)) {
					if ((oldMod == null) || (newMod.URL != oldMod.URL)) {
						if (!newMod.Optional)
							toDownload.Add(Tuple.Create(newMod, oldMod));
						else if ((oldMod == null) && (toDownloadOptional != null))
							toDownloadOptional.Add(newMod);
					}
				} else if ((oldMod != null) && oldMod.Exists)
					toDelete.Add(oldMod);
			}
		}

		void UpdateMods(List<Tuple<Mod, Mod>> toDownload, List<Mod> toDelete)
		{
			Directory.CreateDirectory(Path.Combine(OPdater.Location, "mods"));

			foreach (var newOldPair in toDownload) {
				var newMod = newOldPair.Item1;
				var oldMod = newOldPair.Item2;
				if ((oldMod != null) && oldMod.Exists)
					File.Delete(Path.Combine(OPdater.Location, "mods", oldMod.CurrentFileName));
				File.Move(newMod.TempFile, Path.Combine(OPdater.Location, "mods", newMod.FileName));
			}
			foreach (var mod in toDelete)
				File.Delete(Path.Combine(OPdater.Location, "mods", mod.CurrentFileName));
		}

		#endregion

		#region Entering various things

		void EnterSettings()
		{
			Console.WriteLine("Couldn't find '{0}', creating from scratch.", AppSettings.FileName);

			while (true) {
				string owner, repo;
				var branches = EnterRepository(OPdater.GitHub, out owner, out repo);
				OPdater.Settings.Branch = SelectBranch(branches);
				OPdater.Settings.Owner = owner;
				OPdater.Settings.Repository = repo;
				if (OPdater.VerifyRepository().Result)
					break;

				Console.WriteLine("Repository no worky! Missing '{0}' file...", ModpackInfo.FileName);
				Console.WriteLine();
			}

			Console.WriteLine();
			OPdater.Settings.Save();
		}

		static readonly Regex _githubRegex = new Regex("(?:https?://github.com/)?(?<owner>[a-zA-Z0-9_.-]*)/(?<repo>[a-zA-Z0-9_.-]*)/?");
		static IList<Branch> EnterRepository(IGitHubClient github, out string owner, out string repo)
		{
			IList<Branch> branches;
			while (true) {
				Console.Write("> GitHub Repository: ");
				var match = _githubRegex.Match(Console.ReadLine());
				if (match.Success) {
					owner = match.Groups["owner"].Value;
					repo = match.Groups["repo"].Value;
					try {
						Console.Write("Grabbing repository branches... ");
						branches = new List<Branch>(github.Repository.GetAllBranches(owner, repo).Result);
					} catch {
						Console.WriteLine("ERROR!");
						continue;
					}
					Console.WriteLine("DONE");
					return branches.OrderBy(branch => branch.Name).ToList();
				}
				Console.WriteLine("Enter GitHub repository as 'user/repo' or full URL.");
			}
		}

		static string SelectBranch(IList<Branch> branches)
		{
			var index = Math.Max(branches.IndexOf(branch => (branch.Name == "master")), 0);
			while (true) {
				Console.Write("> Select branch: [{0}] ({1}/{2})", branches[index].Name, index + 1, branches.Count);
				switch (Console.ReadKey(true).Key) {
					case ConsoleKey.LeftArrow:
					case ConsoleKey.UpArrow:
						index = ((index - 1) + branches.Count) % branches.Count;
						break;
					case ConsoleKey.RightArrow:
					case ConsoleKey.DownArrow:
						index = ((index + 1) + branches.Count) % branches.Count;
						break;
					case ConsoleKey.Enter:
						Console.WriteLine();
						return branches[index].Name;
				}
				Console.SetCursorPosition(0, Console.CursorTop);
				Console.Write(new string(' ', Console.WindowWidth - 1));
				Console.SetCursorPosition(0, Console.CursorTop);
			}
		}

		static void SelectOptional(List<Mod> mods)
		{
			Console.WriteLine("Select optional mods:");
			var start = Console.CursorTop;
			var index = 0;
			foreach (var mod in mods)
				Console.WriteLine("    {0} ({1})", mod.Name, mod.Version);
			Console.WriteLine("    DONE");

			while (true) {
				
				Console.SetCursorPosition(0, start + index); Console.Write("[");
				Console.SetCursorPosition(2, start + index); Console.Write("]");

				var key = Console.ReadKey(true).Key;

				Console.SetCursorPosition(0, start + index); Console.Write(" ");
				Console.SetCursorPosition(2, start + index); Console.Write(" ");

				switch (key) {
					case ConsoleKey.UpArrow:
						if (index <= 0) break;
						index--;
						break;
					case ConsoleKey.DownArrow:
						if (index >= mods.Count) break;
						index++;
						break;
					case ConsoleKey.Enter:
						Console.SetCursorPosition(1, start + index);
						if (index < mods.Count)
							Console.Write((mods[index].Enabled = !mods[index].Enabled) ? "X" : " ");
						else { Console.WriteLine("X"); return; }
						break;
				}
			}
		}

		static bool SelectYesNo(bool defaultChoice = true, string yesChoice = "yes", string noChoice = "no")
		{
			var cursor = Console.CursorLeft;
			var choice = defaultChoice;
			while (true) {
				Console.Write("{0}{1}{2}{3}{4}{5}",
					( choice ? "[" : " "), yesChoice, ( choice ? "]" : " "),
					(!choice ? "[" : " "),  noChoice, (!choice ? "]" : " "));
				switch (Console.ReadKey(true).Key) {
					case ConsoleKey.LeftArrow:
					case ConsoleKey.RightArrow:
					case ConsoleKey.DownArrow:
						choice = !choice;
						break;
					case ConsoleKey.Enter:
						Console.WriteLine();
						return choice;
				}
				Console.SetCursorPosition(cursor, Console.CursorTop);
			}
		}

		#endregion
	}
}
