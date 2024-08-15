# WDBX Patching Tool

This is a simple commandline tool that can patch 9.2.7.45745 WoW DB2s based on a JSON patchfile with insert/update instructions that I made to allow for simple db2 editing for applications written in other languages.

For the JSON input format see DBXPatching.Core/Patch.cs

## Usage

`DBXPatchTool {patchFile - path to patch file to apply} {dbcDir - path to directory where dbc data is stored} {optional outputdir - path to directory where output dbc should be written"`