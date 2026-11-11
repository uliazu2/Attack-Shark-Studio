# H510 Studio (teste)

App para o headset Redragon H510 Zeus Pro sem fio (aparece no Windows como "H510-PRO Wireless headset").

A Redragon não tem software para esse modelo, então não existe protocolo conhecido para copiar. O app é dividido assim:

- **Som**: volume e mudo do fone, e um botão para deixar o headset como saída e microfone padrão do Windows.
- **Microfone**: volume, mudo, medidor de nível ao vivo e retorno da própria voz no fone (funciona enquanto o app está aberto).
- **Laboratório**: mostra tudo que o headset manda pela porta interna (VID 040B, PID 0897) e deixa ler relatórios. Também dá para enviar bytes, mas só depois de ligar "Entendo o risco" e confirmar cada envio. Essa porta parece ser a de atualização de firmware, então envie por sua conta.

Som e Microfone usam só o áudio do Windows e não mexem no headset.

## Compilar

Rode `build.cmd` nesta pasta.
