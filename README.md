# promote-build
A rudimentary program for changing both the .NET and Win32 versions of nuget packages

This will only work on OS X with Mono at the moment, and installing wine is a requirement.  This program does two things:

1) Uses Mono.Cecil to change the assembly informational version of the applicable assemblies it finds
2) Uses [ResourceHacker.exe](http://www.angusj.com/resourcehacker/) to change the Win32 metadata version information (this is the stuff that shows up in right click -> Properties menu on Windows)

The reason to do this is for QE purposes.  QE can go through extensive testing on an RC build and give it the green light but then you need to rebuild the software or at the very least gather all the DLLs back together again for repackaging.  This can be cumbersome if a build server is involved and testing is automated.  This program will allow you to simply point at an RC build and have it be rewritten as a prerelease or release (whatever "version string" you specify).

# Details

This is a WIP, and is not very customizable at the moment without forking but there are some issues that needed to be worked though that I will explain here.  They will be useful if trying to fork.

The intended behavior is to download several nuget packages which are dependent on each other 

First of all, Mono.Cecil cannot write a new assembly unless it can find all the dependent assemblies it thinks it needs.  This makes things complicated for nuget packages, which will mix and match during project build but are kept separate at other times.  To get around this, I have written a custom assembly resolver for Cecil which will a) Look in a hardcoded location for Xamarin.iOS.dll (this is an OS X location which explains why this code is OS X only right now) or b) Attempt to find the assembly in another nuget package.  

Next, the reason for Wine is that editing Win32 metadata is difficult, and the only sane way to do it is through the Windows API.  Resource hacker is a nice program, complete with command line, that will get the job done (but only on Windows, or Wine).  Without this step, your old version string will still be present in the Windows properties menu.  
