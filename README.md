## Pack[OP]dater

.. is a tool for downloading and updating modpacks from GitHub repositories,
provided they're made to work with it. It does so by creating a local
repository and downloading mods listed in the repo's `modpack.json` file.

See [copygirl/obsidian-Modpack](https://github.com/copygirl/obsidian-Modpack)
for an example repository.

### Why would anyone use this?

GitHub is an amazing place to host projects of all kinds!

- You can have multiple different versions of a modpack available at a time by
making them different branches.
  - `master` for the most recent stable version.
  - `development` for the all most recent, approved changes.
  - `server` for your testing server that shouldn't update all the time because
    of minor or irrelevant changes.
  - Branches for possibly-not-yet-quite-ready features in development.
- It allows multiple people to easily work on a modpack simultaneously.
- **Issues** can be used as a TODO list, place for suggestions, bug reports and
  general discussions.
- **Pull requests** can be used as a way to allow people not part of the core team
  to provide changes and help with pack maintenance, while also having all the
  upsides of issues.
- There's also **milestones**, **releases** and a **wiki**.

### How does it work?

Put the `PackOPdater.exe` into a fresh Minecraft instance folder. When you're
first starting you'll be promted to enter a repository and select a branch.
This will create a configuration file, so on successive launches you won't
need to enter this information again.
```
Couldn't find 'Pack[OP]dater.json', creating from scratch.
> GitHub Repository: copygirl/obsidian-Modpack
Grabbing repository branches... DONE
> Select branch: [server] (3/3)
```

Now, and every time you launch the updater when there's an update available,
you'll have the option of downloading the latest version of the modpack. If
there are any optional mods, you may select which ones you'd like to include.
```
> Update available, download now? [yes] no
Grabbing latest modpack info... DONE

Select optional mods:
 X  FastCraft (1.21)
    Inventory Tweaks (1.59)
    Mouse Tweaks (2.4.4)
[X] MumbleLink (4.1.1-2b3035b)
    DONE
```

While it is technically possible to includes mods in the GitHub repository
itself instead of putting them in the `modpack.json`, it is highly recommended
**NOT TO DO SO**. Binary files do not work well with git, GitHub should not be
used to host them, and it might be against the mods' redistribution terms.

### Servers

You can also run this tool as a server wrapper. This way, it will check for
updates in the GitHub repository every 2 minutes. When that's the case, it will
notify players of the incoming update and give them some time to log out, shut
down the server gently, apply these updates, and restart the server.

To start the server wrapper, run the program with the following arguments:  
`PackOPdater.exe <server jar> [additional java arguments ...]`  
For example: `PackOPdater.exe minecraftforge.jar -Xmx2G -Xms2G`

### Download

A pre-release version is available on the **[Releases](https://github.com/copygirl/PackOPdater/releases)** page.
Note that this hasn't been thoroughly tested yet, and some useful features might
still be missing.

Should run fine on **Linux** using **Mono** 3.0 and up.
