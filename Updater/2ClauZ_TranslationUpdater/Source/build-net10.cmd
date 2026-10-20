@echo off
rem Сборка под .NET 10 (игра 1.7). Старый build-csc.cmd (csc .NET Framework) больше не подходит.
cd /d "C:\Program Files (x86)\Steam\steamapps\common\RimWorld\Mods\2ClauZ_TranslationUpdater"
set GAME=C:\Program Files (x86)\Steam\steamapps\common\RimWorld
set HARM=C:\Program Files (x86)\Steam\steamapps\workshop\content\294100\2009463077\1.7\Assemblies\0Harmony.dll
set REF=C:\Program Files\dotnet\packs\Microsoft.NETCore.App.Ref\10.0.13\ref\net10.0
set RSP=%TEMP%\tu-build.rsp
echo /nologo /target:library /optimize+ /nostdlib+ /out:Assemblies\TranslationUpdater.dll > "%RSP%"
for %%f in ("%REF%\*.dll") do echo /r:"%%f" >> "%RSP%"
echo /r:"%GAME%\Assembly-CSharp.dll" /r:"%GAME%\UnityStubs.dll" /r:"%GAME%\Waylith.UnityCompat.dll" /r:"%HARM%" >> "%RSP%"
echo Source\TranslationUpdater.cs Source\LangMerge.cs >> "%RSP%"
"C:\Program Files\dotnet\dotnet.exe" "C:\Program Files\dotnet\sdk\10.0.402\Roslyn\bincore\csc.dll" @"%RSP%"
if %errorlevel%==0 (echo BUILD OK) else (echo BUILD FAILED)
