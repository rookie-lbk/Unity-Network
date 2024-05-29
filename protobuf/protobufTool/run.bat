@echo off
for %%i in (proto\*.proto) do (
    protogen.exe -i:%%i -o:cs\%%~ni.cs
)
pause
