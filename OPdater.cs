using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Threading.Tasks;
using NGit.Api;
using NGit.Transport;
using Octokit;
using PackOPdater.Data;

namespace PackOPdater
{
	public class OPdater : IDisposable
	{
		string _latestSha;
		ModpackInfo _latestInfo;

		public string Location { get; private set; }

		public AppSettings Settings { get; private set; }
		public ModpackInfo CurrentModpackInfo { get; private set; }
		public ModpackInfo LatestModpackInfo { get { return (_latestInfo ?? GetLatestModpackInfo().Result); } }

		public IGitHubClient GitHub { get; private set; }
		public NGit.Repository Repository { get; private set; }
		public WebClient WebClient { get; private set; }

		public string CurrentSha { get { return ((Repository != null) ? Repository.Resolve("HEAD").Name : null); } }
		public string LatestSha { get { return (_latestSha ?? GetLatestSha().Result); } }

		public OPdater(string directory)
		{
			Location = directory;

			Settings = AppSettings.Load();
			CurrentModpackInfo = ModpackInfo.Load(directory);

			GitHub = new GitHubClient(new ProductHeaderValue("PackOPdater"));
			if (Directory.Exists(Path.Combine(Location, ".git")))
				Repository = Git.Open(Location).GetRepository();
		}


		public async Task Clone()
		{
			if (Repository != null)
				throw new InvalidOperationException();
			var url = "https://github.com/" + Settings.Owner + "/" + Settings.Repository + ".git";
			await Task.Run(() => {
				var git = Git.Init().SetDirectory(Location).Call();
				Repository = git.GetRepository();

				var config = Repository.GetConfig();
				RemoteConfig remoteConfig = new RemoteConfig(config, "origin");
				remoteConfig.AddURI(new URIish(url));
				remoteConfig.AddFetchRefSpec(new RefSpec(
					"+refs/heads/" + Settings.Branch +
					":refs/remotes/origin/" + Settings.Branch));
				remoteConfig.Update(config);
				config.Save();

				git.Fetch().Call();

				git.BranchCreate().SetName(Settings.Branch).SetStartPoint("origin/" + Settings.Branch)
					.SetUpstreamMode(CreateBranchCommand.SetupUpstreamMode.TRACK).Call();
				git.Checkout().SetName(Settings.Branch).Call();
			});
		}

		public async Task Update()
		{
			if (Repository == null)
				throw new InvalidOperationException();
			var remoteBranch = await GetBranch();
			if (remoteBranch.Commit.Sha != CurrentSha) {
				await Task.Run(() => { 
					var git = new Git(Repository);
					git.Fetch().Call();
					git.Reset().SetRef("origin/" + Settings.Branch)
						.SetMode(ResetCommand.ResetType.HARD).Call();
				});
			}
		}

		public async Task CloneOrUpdate()
		{
			await ((Repository == null) ? Clone() : Update());
			CurrentModpackInfo = ModpackInfo.Load(Location);
		}


		public async Task<bool> VerifyRepository()
		{
			try {
				await GetLatestModpackInfo();
				return true;
			} catch {
				return false;
			}
		}

		public async Task<ModpackInfo> GetLatestModpackInfo()
		{
			var path = (ModpackInfo.FileName + "?ref=" + Settings.Branch);
			var list = await GitHub.Repository.Content.GetAllContents(Settings.Owner, Settings.Repository, path);
			return (_latestInfo = ModpackInfo.Parse(list[0].Content));
		}

		public async Task<string> GetLatestSha()
		{
			await GetBranch();
			return _latestSha;
		}

		public async Task<bool> IsUpdateAvailable()
		{
			return ((Repository == null) || (await GetLatestSha() != CurrentSha));
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


		async Task<Branch> GetBranch()
		{
			var branch = await GitHub.Repository.GetBranch(Settings.Owner, Settings.Repository, Settings.Branch);
			_latestSha = branch.Commit.Sha;
			return branch;
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

