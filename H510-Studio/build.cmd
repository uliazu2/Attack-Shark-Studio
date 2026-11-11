@echo off
rem Compila o H510 Studio com o compilador C# que vem no Windows (.NET Framework 4).
set NET=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319
"%NET%\csc.exe" /nologo /target:winexe /out:H510Studio.exe /lib:"%NET%\WPF" ^
  /r:WindowsBase.dll /r:PresentationCore.dll /r:PresentationFramework.dll /r:System.Xaml.dll ^
  /r:System.Drawing.dll /r:System.Windows.Forms.dll ^
  H510App.cs ..\Shared\UiKit.cs ..\Shared\HidCore.cs H510Core.cs
if %errorlevel%==0 echo Pronto: H510Studio.exe
