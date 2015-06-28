using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using NGit;
using NGit.Api;
using NGit.Revwalk;
using NGit.Transport;
using NGit.Treewalk;
using Octokit;
using PackOPdater.Data;

namespace PackOPdater
{
	public class OPdater : IDisposable
	{
		public string Location { get; private set; }

		public AppSettings Settings { get; private set; }
		public ModpackInfo CurrentModpackInfo { get; private set; }
		public ModpackInfo LatestModpackInfo { get; private set; }

		public NGit.Repository Repository { get; private set; }
		public WebClient WebClient { get; private set; }

		public OPdater(string directory)
		{
			Location = directory;

			Settings = AppSettings.Load();
			CurrentModpackInfo = ModpackInfo.Load(directory);

			if (Directory.Exists(Path.Combine(Location, ".git")))
				Repository = Git.Open(Location).GetRepository();
		}


		public async Task Clone()
		{
			if (Repository != null)
				throw new InvalidOperationException();
			var url = "https://github.com/" + Settings.Owner + "/" + Settings.Repository + ".git";
			var git = Git.Init().SetDirectory(Location).Call();
			Repository = git.GetRepository();

			var config = Repository.GetConfig();
			var remoteConfig = new RemoteConfig(config, "origin");
			remoteConfig.AddURI(new URIish(url));
			remoteConfig.AddFetchRefSpec(new RefSpec(
				"+refs/heads/" + Settings.Branch +
				":refs/remotes/origin/" + Settings.Branch));
			remoteConfig.Update(config);
			config.Save();

			await Fetch();

			await Task.Run(() => {
				git.BranchCreate().SetName(Settings.Branch).SetStartPoint("origin/" + Settings.Branch)
					.SetUpstreamMode(CreateBranchCommand.SetupUpstreamMode.TRACK).Call();
				git.Checkout().SetName(Settings.Branch).Call();
			});
		}

		public async Task Fetch()
		{
			await Task.Run(() => { new Git(Repository).Fetch().Call(); });
		}

		public async Task Update()
		{
			if (Repository == null)
				throw new InvalidOperationException();
			await Task.Run(() => {
				new Git(Repository).Reset().SetRef("origin/" + Settings.Branch)
					.SetMode(ResetCommand.ResetType.HARD).Call();
			});
		}

		public async Task CloneOrUpdate()
		{
			await ((Repository == null) ? Clone() : Update());
			CurrentModpackInfo = ModpackInfo.Load(Location);
		}


		public async Task<bool> VerifyRepository()
		{
			try {
				await DownloadLatestModpackInfo();
				return true;
			} catch {
				return false;
			}
		}

		public async Task<ModpackInfo> DownloadLatestModpackInfo()
		{
			var client = new GitHubClient(new ProductHeaderValue("PackOPdater"));
			var path = ModpackInfo.FileName + "?ref=" + Settings.Branch;
			var contents = await client.Repository.Content.GetAllContents(Settings.Owner, Settings.Repository, path);
			return ModpackInfo.Parse(contents[0].Content);
		}

		public async Task<ModpackInfo> GetLatestModpackInfo()
		{
			if (Repository == null)
				return await DownloadLatestModpackInfo();

			return await Task.Run(() => {
				var id = Repository.Resolve("origin/" + Settings.Branch);
				ObjectReader reader = null;
				try {
					reader = Repository.NewObjectReader();
					var walk = new RevWalk(reader);
					var commit = walk.ParseCommit(id);
					var treeWalk = TreeWalk.ForPath(reader, ModpackInfo.FileName, commit.Tree);
					if (treeWalk == null)
						return null;

					byte[] data = reader.Open(treeWalk.GetObjectId(0)).GetBytes();
					var modpackJson = Encoding.UTF8.GetString(data);
					return ModpackInfo.Parse(modpackJson);
				} finally {
					if (reader != null)
						reader.Release();
				}
			});
		}

		public IEnumerable<RevCommit> GetNewCommits()
		{
			if (Repository == null)
				return Enumerable.Empty<RevCommit>();
			return new Git(Repository).Log().AddRange(
				Repository.Resolve("HEAD"),
				Repository.Resolve("origin/" + Settings.Branch)).Call();
		}

		public async Task<bool> IsUpdateAvailable()
		{
			if (Repository == null)
				return true;
			
			await Fetch();
			return (Repository.Resolve("HEAD").Name !=
			        Repository.Resolve("origin/" + Settings.Branch).Name);
		}

		public async Task<string> Download(Mod mod, Action<int, int> progress)
		{
			DownloadProgressChangedEventHandler handler = (sender, e) =>
				progress((int)e.BytesReceived, (int)e.TotalBytesToReceive);
			var file = Path.GetTempFileName();
			try {
				if (progress != null)
					WebClient.DownloadProgressChanged += handler;
				await WebClient.DownloadFileTaskAsync(mod.URL, file);
			} finally {
				if (progress != null)
					WebClient.DownloadProgressChanged -= handler;
			}
			return file;
		}

		public async Task Download(IEnumerable<Mod> mods, Action<Mod, int, int> progress)
		{
			using (WebClient = new WebClient())
				foreach (var mod in mods) {
					Action<int, int> handler = (a, b) => progress(mod, a, b);
					mod.TempFile = await Download(mod, ((progress != null) ? handler : null));
				}
			WebClient = null;
		}
		public void CancelDownload()
		{
			if (WebClient != null)
				WebClient.CancelAsync();
		}


		#region IDisposable implementation

		public void Dispose()
		{
			if (Repository != null) {
				Repository.Close();
				Repository = null;
			}
			if (WebClient != null) {
				WebClient.Dispose();
				WebClient = null;
			}
		}

		#endregion
	}
}

