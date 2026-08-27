@echo off
REM Demonstra a malha basica com 3 participantes (alice, bob, carol) -- cobre os
REM passos 1-5 do "Roteiro de teste manual" do README, todos na mesma sessao:
REM   1. /list em cada janela -- confirma que os 3 se enxergam (malha completa)
REM   2. Mandar texto livre em qualquer janela -- aparece nas outras duas com o autor
REM   3. /msg bob mensagem privada (em alice) -- so aparece em bob, nao em carol
REM   4. Colar uma rajada de linhas curtas + uma mensagem bem longa -- nada trunca/gruda
REM   5. /quit numa janela -- as outras duas anunciam a saida e continuam normalmente
REM   6. Fechar uma janela na marra (botao X) -- as outras detectam a queda e nao travam

cd /d "%~dp0"
dotnet build -v q

echo.
echo Abrindo alice, bob e carol (127.0.0.1:9001/9002/9003)...
echo.

start "alice (9001)" cmd /k dotnet bin\Debug\net9.0\ChatDistribuido.dll --port 9001 --name alice --peers 127.0.0.1:9002,127.0.0.1:9003
start "bob (9002)"   cmd /k dotnet bin\Debug\net9.0\ChatDistribuido.dll --port 9002 --name bob   --peers 127.0.0.1:9001,127.0.0.1:9003
start "carol (9003)" cmd /k dotnet bin\Debug\net9.0\ChatDistribuido.dll --port 9003 --name carol --peers 127.0.0.1:9001,127.0.0.1:9002

echo.
echo Rode /list em cada janela para confirmar a malha completa, depois siga os
echo passos do "Roteiro de teste manual" no README (mensagem, /msg, /quit, etc.).
