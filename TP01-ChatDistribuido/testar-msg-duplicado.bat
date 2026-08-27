@echo off
REM Demonstra a desambiguacao do /msg quando duas pessoas tem o mesmo apelido.
REM servidor tem dois peers chamados "bob" conectados (bob1 e bob2, mas ambos
REM se identificam como "bob"). No terminal do servidor, rode:
REM   /msg bob oi
REM Deve aparecer uma lista numerada com as duas opcoes -- digite 1 ou 2 e
REM confira, no terminal do bob correspondente, que so ele recebeu a mensagem.

cd /d "%~dp0"
dotnet build -v q

echo.
echo Abrindo servidor + dois peers chamados "bob"...
echo.

start "servidor" cmd /k dotnet bin\Debug\net9.0\ChatDistribuido.dll --port 9001 --name servidor --host 127.0.0.1
timeout /t 2 >nul
start "bob1 (apelido: bob)" cmd /k dotnet bin\Debug\net9.0\ChatDistribuido.dll --port 9002 --name bob --host 127.0.0.1 --peers 127.0.0.1:9001
start "bob2 (apelido: bob)" cmd /k dotnet bin\Debug\net9.0\ChatDistribuido.dll --port 9003 --name bob --host 127.0.0.1 --peers 127.0.0.1:9001

echo.
echo No terminal "servidor", espere a mensagem de conexao dos dois "bob" e rode:
echo   /msg bob oi
echo Escolha 1 ou 2 na lista numerada e confira em qual janela "bob" a mensagem chegou.
