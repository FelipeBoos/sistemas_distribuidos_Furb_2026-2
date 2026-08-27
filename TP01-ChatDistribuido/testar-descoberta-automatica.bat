@echo off
REM Demonstra a descoberta automatica SEM passar --peers: cada no so informa seu
REM proprio --host e --port, e o proprio app varre sozinho a sub-rede derivada
REM do --host (os 3 primeiros octetos) nessa mesma porta -- equivalente a
REM "--peers <minha-sub-rede>.*:<minha-porta>" implicito.
REM
REM Windows trata todo o bloco 127.0.0.0/8 como loopback, entao 127.0.1.x
REM funciona sem nenhuma configuracao extra de rede.

cd /d "%~dp0"
dotnet build -v q

echo.
echo Abrindo 3 terminais em 127.0.1.11/12/13:9001, NENHUM com --peers.
echo Espere alguns segundos e rode /list em cada um: os 3 devem se achar sozinhos,
echo so por estarem na mesma faixa 127.0.1.* e na mesma porta 9001.
echo.

start "no1 (127.0.1.11) - sem --peers" cmd /k dotnet bin\Debug\net9.0\ChatDistribuido.dll --port 9001 --name no1 --host 127.0.1.11
start "no2 (127.0.1.12) - sem --peers" cmd /k dotnet bin\Debug\net9.0\ChatDistribuido.dll --port 9001 --name no2 --host 127.0.1.12
start "no3 (127.0.1.13) - sem --peers" cmd /k dotnet bin\Debug\net9.0\ChatDistribuido.dll --port 9001 --name no3 --host 127.0.1.13
