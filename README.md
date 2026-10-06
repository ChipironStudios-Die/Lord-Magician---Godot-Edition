# Lord Magician - migración a Godot 4 (GDScript)

Este es un proyecto de Godot preparado a partir del juego Android/Kotlin entregado. Se ha elegido conservar el raycaster pseudo-3D en GDScript dentro de Godot: es la opción que mantiene el aspecto del gameplay (paredes por columnas, enemigos como sprites, arma en primer plano, HUD y CRT), pero elimina las dependencias de Android/Compose.

## Qué se ha migrado

- Los 8 niveles, sus mapas de 16 x 16, puntos de inicio, enemigos, misiones y recompensas.
- Las armas, armaduras, accesorios, economía, experiencia y subida de nivel.
- Movimiento con colisiones, raycasting, profundidad de paredes y sprites, proyectiles, partículas y vibración de cámara.
- IA de enemigos cuerpo a cuerpo, a distancia, tanque, jefe y centinela; incluye línea de visión y los ataques especiales.
- Menú, ajustes, pausa, fin de nivel, tienda, derrota y victoria.
- Ratón/teclado, mando y controles táctiles dibujados en pantalla.
- Música, efectos existentes, sprites originales y un shader CRT equivalente al filtro de Compose.

## Abrirlo por primera vez

1. Instala Godot 4.7 o posterior (la descarga estándar: ya no hace falta la edición **.NET** ni el SDK de .NET).
2. En Godot, pulsa **Importar** y elige `project.godot` de esta carpeta.
3. Espera a que Godot importe los PNG, JPG y audio, y pulsa **F5** (o el botón de ejecutar proyecto).

## Controles

| Acción | Teclado/ratón | Mando | Táctil |
| --- | --- | --- | --- |
| Mover | WASD | Stick izquierdo | Joystick inferior izquierdo |
| Mirar | Mantén botón derecho y mueve el ratón | Stick derecho | Arrastra la parte derecha de la pantalla |
| Disparar | Espacio o botón izquierdo | R2 / RB | Botón `DISPARAR` |
| Pausa | Escape o P | Start/Back | Botón `II` |
| Menús | Flechas y Enter | Cruceta y A | Botones en pantalla |

## Dónde editar cada parte

| Si quieres cambiar... | Edita... |
| --- | --- |
| Nivel, enemigos, armas o tienda | `scripts/GameData.gd` |
| Combate, IA, input, raycaster o pantallas | `scripts/GameMain.gd` |
| Intensidad del efecto retro | `shaders/crt_overlay.gdshader` |
| Sprites, logotipo y arma | `assets/sprites/` |
| Música y efectos | `assets/audio/` |
| Resolución, orientación y renderizador | `project.godot` |

## Decisiones y límites conocidos

- Se mantiene un raycaster dibujado por código en lugar de reconstruir los mapas como mallas 3D. Esto hace que el juego conserve el aspecto de la versión Android y permite comparar mecánicas sin rediseñar niveles.
- El sonido `snd_potion` se solicitaba en el Kotlin original, pero no venía dentro de `res/raw`; por eso no se inventó ni se sustituyó por otro archivo. Puedes añadir `assets/audio/snd_potion.mp3` y reproducirlo en la recogida de objetos si lo tienes.
- El proyecto es GDScript puro: no necesita la edición .NET de Godot y la exportación web, que Godot 4 no permite con C#, vuelve a ser posible (el multijugador por ENet no funciona en Web).
- La traducción de C# a GDScript se comprobó con Godot 4.7.2 en modo headless (sin GPU): menús, los 8 niveles, combate, tienda, controles y multijugador por IP. El aspecto visual 3D no se pudo ver sin pantalla, así que conviene revisarlo al abrirlo en el editor.

## Diferencia respecto a la app Android

`MainActivity.kt` mezclaba ciclo de vida Android, UI Compose, audio nativo y la lógica del juego. En esta versión Godot se encarga del ciclo de vida, la pantalla completa, audio e inputs; `GameMain.gd` concentra la simulación y el dibujo. Esto deja la puerta abierta a separar más adelante el juego en escenas/nodos individuales sin cambiar las reglas del juego.
