# K86 Studio

Programa para configurar o teclado Attack Shark K86 no Windows, no lugar do app oficial.

Dá pra mexer em:

- efeitos de luz (25 no total), brilho, velocidade e cor
- cor de cada tecla separada
- modo música, que pega o som do PC e manda pro teclado
- modo cor da tela, que acompanha a cor do monitor
- função de cada tecla (outra tecla, atalho, mídia, mouse ou macro), salva no próprio teclado
- macros
- foto ou GIF na telinha e o relógio dela
- tempo pra apagar a luz, debounce, trava da tecla Windows e troca de WASD com as setas

## Compilar

Não precisa instalar nada, o compilador de C# já vem no Windows. É só rodar:

```
build.cmd
```

Vai sair o `K86Studio.exe`. O teclado tem que estar no cabo USB e o app oficial fechado.

## Arquivos

- `K86App.cs`: a janela e as telas
- `K86Backend.cs`: o que lê e grava no teclado
- `K86Core.cs`: os comandos do K86 e a captura de som e de tela
- `../Shared/UiKit.cs`: visual e animações (usado pelos dois apps)
- `../Shared/HidCore.cs`: comunicação USB (usado pelos dois apps)

## Como funciona

O K86 usa uma placa da Rongyuan (VID 3151, PID 4015 no cabo). Tudo é feito com relatórios de feature de 64 bytes. A lista dos comandos está no começo do `K86App.cs`. Descobri os comandos gravando o tráfego USB do app oficial e lendo o código do driver da Attack Shark.

Não é um projeto oficial da Attack Shark. Se der algum problema, o reset de fábrica nos Ajustes volta tudo ao normal.
