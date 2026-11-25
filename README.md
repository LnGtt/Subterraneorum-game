# Subterraneorum

Dungeon crawler 2.5D **cooperativo local para até 4 jogadores**. Escolham seus heróis, desçam por uma masmorra gerada a cada partida, votem juntos nas cartas de poder e enfrentem o Guardião Abissal no último andar.

**Trailer:** _(cole aqui o link do YouTube)_

## Como abrir

1. Instale o **Unity 6000.5.6f1** pelo Unity Hub.
2. No Hub: **Add › Add project from disk** e escolha esta pasta.
3. Abra a cena `Assets/Scenes/MainMenu.unity` e aperte **Play**.

Na primeira abertura o Unity importa os assets e baixa os pacotes (URP, Input System, Cinemachine, AI Navigation). Isso pode levar alguns minutos.

## Como jogar

Na tela de seleção, cada jogador aperta um botão no teclado ou no próprio controle para entrar (até 4). Cada um escolhe uma classe diferente:

| Herói | Classe | Estilo |
|---|---|---|
| Nanus Ferox | Guerreiro | linha de frente, escudo e golpe pesado |
| Aquila | Arqueira | ataque à distância e mobilidade |
| Pollienus | Mago | magia em área e cura |
| Sangarinus | Assassino | golpes rápidos que roubam vida |

| Ação | Teclado | Controle |
|---|---|---|
| Mover | WASD | analógico |
| Atacar | clique / J | X |
| Esquiva | Espaço | A / RB |
| Habilidade 1 | K / botão direito | B / RT |
| Habilidade 2 | L / Q | LB / LT |
| Árvore de habilidades | Tab | Select |
| Reviver aliado / minijogo | segurar E / E | segurar Y / Y |
| Pausa | Esc | Start |

- **Cartas:** ao subir de nível, todos votam numa carta, e a escolha precisa ser unânime. As que sobram fortalecem os inimigos.
- **Zona segura:** cura o grupo e tem o minijogo das orbes, em que o vencedor rouba XP dos outros.
- **Andares:** são 3. O chefão espera no último.
- **Volume:** música e efeitos se ajustam no menu de pausa.

## Tecnologia

Unity 6 · URP · Input System (multiplayer local) · Cinemachine 3 · AI Navigation. Dungeon procedural gerada em tempo de execução. Trilha sonora adaptativa e efeitos sonoros originais.
