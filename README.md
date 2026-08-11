# Lord Magician - migración a Godot 4 con C#

Este es un proyecto de Godot preparado a partir del juego Android/Kotlin entregado. Se ha elegido conservar el raycaster pseudo-3D en C# dentro de Godot: es la opción que mantiene el aspecto del gameplay (paredes por columnas, enemigos como sprites, arma en primer plano, HUD y CRT), pero elimina las dependencias de Android/Compose.

## Qué se ha migrado

- Los 8 niveles, sus mapas de 16 x 16, puntos de inicio, enemigos, misiones y recompensas.
- Las armas, armaduras, accesorios, economía, experiencia y subida de nivel.
- Movimiento con colisiones, raycasting, profundidad de paredes y sprites, proyectiles, partículas y vibración de cámara.
- IA de enemigos cuerpo a cuerpo, a distancia, tanque, jefe y centinela; incluye línea de visión y los ataques especiales.
- Menú, ajustes, pausa, fin de nivel, tienda, derrota y victoria.
- Ratón/teclado, mando y controles táctiles dibujados en pantalla.
- Música, efectos existentes, sprites originales y un shader CRT equivalente al filtro de Compose.

## Abrirlo por primera vez

1. Instala la edición **.NET** de Godot 4.5 o posterior y el SDK de .NET que te pida Godot. No uses la descarga estándar de Godot: esa no compila scripts C#.
2. En Godot, pulsa **Importar** y elige `project.godot` de esta carpeta.
3. Espera a que Godot importe los PNG, JPG y MP3. El archivo `LordMagicianGodot.csproj` ya está incluido; no hace falta crear un script vacío para que Godot prepare C#.
4. Pulsa **Build** en el editor y después **F6** (o el botón de ejecutar proyecto). Si has abierto el proyecto antes de recibir esta corrección, ciérralo y ábrelo de nuevo para que detecte el `.csproj`.

Si Godot muestra que falta el SDK, instala el SDK de .NET de 64 bits y reinicia el editor. Para escritorio, Godot 4.5 requiere .NET 8 o posterior. Para exportar esta variante C# a Android, instala además .NET 9 o posterior; ese soporte sigue marcado como experimental por Godot.

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
| Nivel, enemigos, armas o tienda | `scripts/GameData.cs` |
| Combate, IA, input, raycaster o pantallas | `scripts/GameMain.cs` |
| Intensidad del efecto retro | `shaders/crt_overlay.gdshader` |
| Sprites, logotipo y arma | `assets/sprites/` |
| Música y efectos | `assets/audio/` |
| Resolución, orientación y renderizador | `project.godot` |

## Decisiones y límites conocidos

- Se mantiene un raycaster dibujado por código en lugar de reconstruir los mapas como mallas 3D. Esto hace que el juego conserve el aspecto de la versión Android y permite comparar mecánicas sin rediseñar niveles.
- El sonido `snd_potion` se solicitaba en el Kotlin original, pero no venía dentro de `res/raw`; por eso no se inventó ni se sustituyó por otro archivo. Puedes añadir `assets/audio/snd_potion.mp3` y reproducirlo en la recogida de objetos si lo tienes.
- El proyecto está preparado para escritorio, Android y iOS con la edición .NET de Godot. La exportación web no está disponible para proyectos C# de Godot 4; si Web es una plataforma imprescindible, conviene portar los dos `.cs` a GDScript o mantener una variante Kotlin/LibGDX.
- En el equipo donde se preparó esta entrega no había instalados Godot ni el SDK de .NET, así que el proyecto no se ha podido ejecutar aquí. Se verificaron la estructura del proyecto, los recursos copiados, los niveles y el equilibrio sintáctico de los archivos C#; la compilación final debe hacerse al abrirlo en Godot .NET.

## Diferencia respecto a la app Android

`MainActivity.kt` mezclaba ciclo de vida Android, UI Compose, audio nativo y la lógica del juego. En esta versión Godot se encarga del ciclo de vida, la pantalla completa, audio e inputs; `GameMain.cs` concentra la simulación y el dibujo. Esto deja la puerta abierta a separar más adelante el juego en escenas/nodos individuales sin cambiar las reglas del juego.
