# X11 Studio

Programa para configurar o mouse Attack Shark X11 no Windows. Funciona pelo receptor sem fio ou pelo cabo.

Dá pra mexer em:

- DPI dos 8 estágios (50 a 22.000) e a cor de cada um
- LOD, Motion Sync, correção de ângulo e ripple control
- luz do mouse (7 efeitos), cor e brilho
- função de cada botão, inclusive teclas e atalhos do teclado
- taxa de resposta (125 a 1000 Hz) e debounce do clique
- tempo pra apagar a luz e pra dormir

Também mostra a bateria.

## Compilar

Não precisa instalar nada, o compilador de C# já vem no Windows:

```
build.cmd
```

Vai sair o `X11Studio.exe`.

## Arquivos

- `X11App.cs`: a janela, as telas e a parte que fala com o mouse
- `X11Core.cs`: os comandos do X11
- `../Shared/UiKit.cs`: visual e animações (usado pelos dois apps)
- `../Shared/HidCore.cs`: comunicação USB (usado pelos dois apps)

## Créditos

Os comandos do X11 vieram do [attack-shark-x11-driver](https://github.com/HarukaYamamoto0/attack-shark-x11-driver), do HarukaYamamoto0 (licença MIT). A licença dele está em `THIRD_PARTY_LICENSE-attack-shark-x11-driver.txt`.

Não é um projeto oficial da Attack Shark.
