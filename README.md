
# WebSocket => App Chat

O código a seguir explora API WebSockets de baixo nível em .net core 3.0
Com o WebSockets é possível enviar dados entre cliente e servidor por meio de uma única conexão TCP.

Esse projeto lida com todo o código em baixo nível e com conexões individuais, buffers e tokens.

Middleware .net core para criação de Chat em tempo real
----------------------------------------

Essa aplicação utiliza as seguites tecnologias:

* .net Core 3.0
* JavaScript
* JSON


WebSocket sem utilização de Bibliotecas
------------------------------------

Para resolução do problema aqui proposto foi utilizado unicamente o protocolo WebSocket sem utilização de bibliotecas como SignalR por exemploa para demostração da implementação de um serviço Middleware servidor de comunicação e a comunicação entre diversos clientes com troca de mensagem.


Estrutura do projeto
----------------------------------------------

O projeto ChatWebSocket foi criado utilizando a seguinte estrutura:

1. CWS.Domain.Shared => Projeto utilizado para classes compartilhadas dentro de todo o projeto como Enums, conversão de Json, classes de descrição (models) para descrição e resultado de chamadas, mensagens e exceções remotas além de arquivos de definição de estratégias de invocação de controllers, métodos de chamada e strings.
2. CWS.Infra.Data => Projeto para conexão com os métodos de abertura de conexÃo clienteWebSocket e também faz o controle de tarefas de envio e recebimento assícronas de mensagens.
3. CWS.ApplicationService => Projeto que faz o controle da principal classe do projeto WebSocketManagerMiddleware que é a responsável por eventos de conexão e desconexão, além do envio e recebimento de mensagens - Invoke e Receive. Dentro desse projeto também estão as classes de ConnectionManager que faz a gestão da conexão, a classe Handler faz o controle se um WebSocket está conectado, desconectado, tem métodos para recebimento assíncrono de mensagem com sobreposição de métodos para atender a diferentes tipos de recebimento de mensagem, envio de mensagem para um grupo específico e recebimento.
5. CWS.Tests => Classe que faz a cobertura de testes para os métodos da aplicação:
6. CWS.Web => O projeto Web utiliza os métodos disponíveis na ApplicationService e cria uma página Web (EnterChat.html) aonde o usuário pode inserir o nome de usuário e será direcionado para a tela de demostração do chat.

Inicializando o projeto **CWS.Web**
--------------------------------------

Para iniciar o projeto de demostração basta rodar o projeto CWS.Web e acessar o endereço:

http://localhost:65110/EnterChat.html

Após isso basta inserir um nome de usuário, clicar em entrar e será redirecionado para a tela do Chat.
Abra mais de uma instância com nomes de usuários diferentes para acompanhar o funcionamento da aplicação.


<img width="1667" alt="Captura de Tela 2021-03-28 às 21 18 05" src="https://user-images.githubusercontent.com/69854207/112773105-18337e80-900b-11eb-991e-c6b02a127924.png">
