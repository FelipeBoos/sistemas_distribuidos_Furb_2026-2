@echo off
REM Demonstra a varredura de sub-rede (--peers 127.0.1.*:9001): 3 nos entram so
REM sabendo a faixa de IP, sem listar cada um individualmente.
REM Windows trata todo o bloco 127.0.0.0/8 como loopback, entao 127.0.1.x
REM funciona sem nenhuma configuracao extra de rede.
REM
REM Porta 9001 (familia "900*") porque isso e trafego DENTRO da mesma rede --
REM a porta fixa 5000 e reservada so para ENTRAR numa rede diferente da sua
REM (ver testar-ponte-redes.bat).

cd /d "%~dp0"
dotnet build -v q

echo.
echo Abrindo 3 terminais na faixa 127.0.1.11-13, todos so com --peers 127.0.1.*:9001
echo Espere alguns segundos e rode /list em cada um: os 3 devem se achar sozinhos.
echo.

start "no1 (127.0.1.11)" cmd /k dotnet bin\Debug\net9.0\ChatDistribuido.dll --port 9001 --name no1 --host 127.0.1.11 --peers 127.0.1.*:9001
start "no2 (127.0.1.12)" cmd /k dotnet bin\Debug\net9.0\ChatDistribuido.dll --port 9001 --name no2 --host 127.0.1.12 --peers 127.0.1.*:9001
start "no3 (127.0.1.13)" cmd /k dotnet bin\Debug\net9.0\ChatDistribuido.dll --port 9001 --name no3 --host 127.0.1.13 --peers 127.0.1.*:9001
