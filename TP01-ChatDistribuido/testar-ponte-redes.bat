@echo off
REM Demonstra a ponte entre duas "redes" (simuladas com 127.0.1.* e 127.0.2.*)
REM e a regra fixa da porta 5000 ao entrar numa rede diferente da sua.
REM
REM Convencao de portas:
REM   - Dentro da MESMA rede: familia "900*" (9001, 9002, 9003...) -- porta normal.
REM   - Para ENTRAR numa rede diferente: sempre 5000 -- por isso servidorA (o
REM     "peer-ponte" da rede A, o unico que rede B vai discar diretamente) roda
REM     nessa porta. Os demais membros da rede A usam 900* normalmente.
REM   - Uma vez dentro, os peers aprendidos via gossip (PeerList) sao discados na
REM     porta real deles (900*), sem forcar 5000 de novo -- e por isso nodeB
REM     consegue falar com nodeA2/nodeA3 direto, nao so com servidorA.
REM
REM Rede A: servidorA (127.0.1.11:5000, sem peers -- o "portao" da rede A) +
REM nodeA2 (127.0.1.12:9002) + nodeA3 (127.0.1.13:9003), ambos apontando para
REM servidorA:5000.
REM Rede B: nodeB (127.0.2.20:9010) entra so pelo IP do servidorA (sem porta!) --
REM a porta usada eh sempre 5000 por ser de outra sub-rede -- e depois descobre
REM nodeA2/nodeA3 sozinho, nas portas reais deles, via a lista de peers que
REM servidorA compartilha (PeerList).

cd /d "%~dp0"
dotnet build -v q

echo.
echo Passo 1: montando a rede A (servidorA:5000, nodeA2:9002, nodeA3:9003)...
echo.

start "servidorA (rede A, 127.0.1.11:5000)" cmd /k dotnet bin\Debug\net9.0\ChatDistribuido.dll --port 5000 --name servidorA --host 127.0.1.11
timeout /t 2 >nul
start "nodeA2 (rede A, 127.0.1.12:9002)" cmd /k dotnet bin\Debug\net9.0\ChatDistribuido.dll --port 9002 --name nodeA2 --host 127.0.1.12 --peers 127.0.1.11:5000
start "nodeA3 (rede A, 127.0.1.13:9003)" cmd /k dotnet bin\Debug\net9.0\ChatDistribuido.dll --port 9003 --name nodeA3 --host 127.0.1.13 --peers 127.0.1.11:5000

echo.
echo Passo 2 (aguardando 5s a rede A se formar): abrindo nodeB (rede B, 127.0.2.20:9010)
echo apontando so para o IP do servidorA (sem porta) -- ele deve entrar em 5000
echo automaticamente e, pouco depois, enxergar nodeA2 (9002) e nodeA3 (9003)
echo tambem no /list, mesmo sem ter sido configurado com o endereco deles.
echo.
timeout /t 5 >nul

start "nodeB (rede B, 127.0.2.20:9010)" cmd /k dotnet bin\Debug\net9.0\ChatDistribuido.dll --port 9010 --name nodeB --host 127.0.2.20 --peers 127.0.1.11

echo.
echo Rode /list em cada janela para conferir. Em nodeB, /list deve mostrar
echo servidorA, nodeA2 e nodeA3 -- apesar de so termos passado o IP do servidorA.
