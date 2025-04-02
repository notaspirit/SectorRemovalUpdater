# WolvenkitCSharpCliAppTemplate
Simple template to get started with a custom C# app using WolvenKits Nuget Packages

## How to use this template
### Get Started Coding
- Download the repository e.g. with `git clone https://github.com/notaspirit/WolvenkitCSharpCliAppTemplate` or by downloading the main branch as a zip via the github webui
- It is recommended to set up your own github repo for version control
- Rename all instances of "YOUR_APPNAME" with your desired app name
- It is recommended to create a new `.cs` file in the `Services` folder (this is where your logic will go)
- add the new command to the switch in handle command if you want to add it as part of the interactive section (so it has access to wkit functionality)
- build either with `dotnet publish` or through your ide

### Get Started Using
- In the directory with the built `.exe` run `YOUR_APPNAME.exe help` to get a list of commands, `YOUR_APPNAME.exe start {EXE GAME PATH} {ENABLE MODS true : false (optional, default false)}` will start the interactive mode which has it's own set of commands with access to WolvenKits functionality.
- To make your app a global command you will need to add the directory to the PATH variables

## Additional Tips
- For a simple multithreading solution build an array or list of Task<> and use Task.WhenAll() to wait for their completion