using System.Collections;
using System.Collections.Generic;
using UnityEngine;

using System;
using System.Text;
using System.Net.Sockets;
using System.Net;
using System.Threading;
using System.Text.RegularExpressions;
using System.IO;

public static class StaticServerSharedInfo {
    public volatile static bool stoped;
    public static int counter;
};

public class HTTPServer : MonoBehaviour {
    // Класс-обработчик клиента
    class Client {
        // Отправка страницы с ошибкой
        private void SendError(TcpClient Client, int Code) {
            // Получаем строку вида "200 OK"
            // HttpStatusCode хранит в себе все статус-коды HTTP/1.1
            string CodeStr = Code.ToString() + " " + ((HttpStatusCode)Code).ToString();
            // Код простой HTML-странички
            string Html = "<html><body><h1>UNITY SERVER:" + CodeStr + "</h1></body></html>";
            // Необходимые заголовки: ответ сервера, тип и длина содержимого. После двух пустых строк - само содержимое
            string Str = "HTTP/1.1 " + CodeStr + "\nContent-type: text/html\nContent-Length:" + Html.Length.ToString() + "\n\n" + Html;
            // Приведем строку к виду массива байт
            byte[] Buffer = Encoding.ASCII.GetBytes(Str);
            // Отправим его клиенту
            Client.GetStream().Write(Buffer, 0, Buffer.Length);
            // Закроем соединение
            Client.Close();
        }

        // Конструктор класса. Ему нужно передавать принятого клиента от TcpListener
        public Client(TcpClient Client) {
            // Объявим строку, в которой будет хранится запрос клиента
            string Request = "";
            // Буфер для хранения принятых от клиента данных
            byte[] Buffer = new byte[1024];
            // Переменная для хранения количества байт, принятых от клиента
            int Count;
            // Читаем из потока клиента до тех пор, пока от него поступают данные
            while ((Count = Client.GetStream().Read(Buffer, 0, Buffer.Length)) > 0) {
                // Преобразуем эти данные в строку и добавим ее к переменной Request
                Request += Encoding.ASCII.GetString(Buffer, 0, Count);
                // Запрос должен обрываться последовательностью \r\n\r\n
                // Либо обрываем прием данных сами, если длина строки Request превышает 4 килобайта
                // Нам не нужно получать данные из POST-запроса (и т. п.), а обычный запрос
                // по идее не должен быть больше 4 килобайт
                if (Request.IndexOf("\r\n\r\n") >= 0 || Request.Length > 4096) {
                    break;
                }
            }

            // Парсим строку запроса с использованием регулярных выражений
            // При этом отсекаем все переменные GET-запроса
            Match ReqMatch = Regex.Match(Request, @"^\w+\s+([^\s\?]+)[^\s]*\s+HTTP/.*|");

            // Если запрос не удался
            if (ReqMatch == Match.Empty) {
                // Передаем клиенту ошибку 400 - неверный запрос
                SendError(Client, 400);
                return;
            }

            // Получаем строку запроса
            string RequestUri = ReqMatch.Groups[1].Value;

            // Приводим ее к изначальному виду, преобразуя экранированные символы
            // Например, "%20" -> " "
            RequestUri = Uri.UnescapeDataString(RequestUri);

            // Если в строке содержится двоеточие, передадим ошибку 400
            // Это нужно для защиты от URL типа http://example.com/../../file.txt
            if (RequestUri.IndexOf("..") >= 0) {
                SendError(Client, 400);
                return;
            }

            // Если строка запроса оканчивается на "/", то отправим страницу данных сервера
            if (RequestUri.EndsWith("/")) {
                SendSharedInfo(Client);
                return;
            }

            if (RequestUri.EndsWith("/stop")) {
                SendSharedInfo(Client);   
                server.STOP = true;
#if UNITY_EDITOR
                // Application.Quit() does not work in the editor so
                // UnityEditor.EditorApplication.isPlaying need to be set to false to end the game
                UnityEditor.EditorApplication.isPlaying = false;
#else
         Application.Quit();
#endif
                return;
            }

            string FilePath = "www/" + RequestUri;

            // Если в папке www не существует данного файла, посылаем ошибку 404
            if (!File.Exists(FilePath)) {
                SendError(Client, 404);
                return;
            }

            // Получаем расширение файла из строки запроса
            string Extension = RequestUri.Substring(RequestUri.LastIndexOf('.'));

            // Тип содержимого
            string ContentType = "";

            // Пытаемся определить тип содержимого по расширению файла
            switch (Extension) {
                case ".htm":
                case ".html":
                    ContentType = "text/html";
                    break;
                case ".css":
                    ContentType = "text/stylesheet";
                    break;
                case ".js":
                    ContentType = "text/javascript";
                    break;
                case ".jpg":
                    ContentType = "image/jpeg";
                    break;
                case ".jpeg":
                case ".png":
                case ".gif":
                    ContentType = "image/" + Extension.Substring(1);
                    break;
                default:
                    if (Extension.Length > 1) {
                        ContentType = "application/" + Extension.Substring(1);
                    }
                    else {
                        ContentType = "application/unknown";
                    }
                    break;
            }

            // Открываем файл, страхуясь на случай ошибки
            FileStream FS;
            try {
                FS = new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            }
            catch (Exception) {
                // Если случилась ошибка, посылаем клиенту ошибку 500
                SendError(Client, 500);
                return;
            }

            // Посылаем заголовки
            string Headers = "HTTP/1.1 200 OK\nContent-Type: " + ContentType + "\nContent-Length: " + FS.Length + "\n\n";
            byte[] HeadersBuffer = Encoding.ASCII.GetBytes(Headers);
            Client.GetStream().Write(HeadersBuffer, 0, HeadersBuffer.Length);

            // Пока не достигнут конец файла
            while (FS.Position < FS.Length) {
                // Читаем данные из файла
                Count = FS.Read(Buffer, 0, Buffer.Length);
                // И передаем их клиенту
                Client.GetStream().Write(Buffer, 0, Count);
            }

            // Закроем файл и соединение
            FS.Close();
            Client.Close();
        }

        void SendSharedInfo(TcpClient Client) {
            string StaticServerSharedInfo_counter = StaticServerSharedInfo.counter.ToString();
            string StaticServerSharedInfo_stoped = StaticServerSharedInfo.stoped.ToString();
            string Html = "<html><body><h1>Counter:" + StaticServerSharedInfo_counter + 
                            "</h1><h1>Stop:" + StaticServerSharedInfo_stoped +
                            "</h1>" +
                            "<div><a href='http://192.168.0.11/asd'> goto 404</a></div>" +
                            "<div><a href='http://192.168.0.11'> refresh</a></div>" +
                            "<div><a href='http://192.168.0.11/stop'> stop and quit</a></div>" +
                            "</body></html>";
            string Str = "HTTP/1.1 200 OK\nContent-type: application/unknown\nContent-Length:" + Html.Length.ToString() + "\n\n" + Html;
            byte[] Buffer = Encoding.ASCII.GetBytes(Str);
            Client.GetStream().Write(Buffer, 0, Buffer.Length);
            Client.Close();
        }
    }

    class Server {
        public bool STOP;
        public TcpListener Listener; // Объект, принимающий TCP-клиентов
        Thread ServerThread;

        // Запуск сервера
        public void Start(int Port) {
            Listener = new TcpListener(IPAddress.Any, Port); // Создаем "слушателя" для указанного порта
            Listener.Start(); // Запускаем его
            STOP=false;
            ServerThread = new Thread(delegate () { serverthread(); });
            ServerThread.Start();
         }

        void serverthread() {
            while (!STOP) {
                // Принимаем новых клиентов. После того, как клиент был принят, он передается в новый поток (ClientThread)
                // с использованием пула потоков.
                ThreadPool.QueueUserWorkItem(new WaitCallback(ClientThread), Listener.AcceptTcpClient());           //или через пул или через ассепт

                /*// Принимаем нового клиента
                TcpClient Client = Listener.AcceptTcpClient();
                // Создаем поток
                Thread Thread = new Thread(new ParameterizedThreadStart(ClientThread));
                // И запускаем этот поток, передавая ему принятого клиента
                Thread.Start(Client);*/

            }
            Listener.Stop();
            StaticServerSharedInfo.stoped = true;
        }

        static void ClientThread(System.Object StateInfo) {
            // Просто создаем новый экземпляр класса Client и передаем ему приведенный к классу TcpClient объект StateInfo
            new Client((TcpClient)StateInfo);
        }

        // Остановка сервера
        ~Server() {
            // Если "слушатель" был создан
            if (Listener != null) {
                // Остановим его
                Listener.Stop();
            }
        }

        /*static void Main(string[] args) {
            // Определим нужное максимальное количество потоков
            // Пусть будет по 4 на каждый процессор
            int MaxThreadsCount = Environment.ProcessorCount * 4;
            // Установим максимальное количество рабочих потоков
            ThreadPool.SetMaxThreads(MaxThreadsCount, MaxThreadsCount);
            // Установим минимальное количество рабочих потоков
            ThreadPool.SetMinThreads(2, 2);
            // Создадим новый сервер на порту 80
            new Server(80);
        } */

    }

    //UNITY ===================================================================================================================

    static Server server=new Server();
    // Use this for initialization
    void Start() {
        int MaxThreadsCount = Environment.ProcessorCount * 4;
        ThreadPool.SetMaxThreads(MaxThreadsCount, MaxThreadsCount);
        ThreadPool.SetMinThreads(2, 2);
        server.Start(80);
    }

    // Update is called once per frame
    void FixedUpdate() {
        StaticServerSharedInfo.counter++;
    }

    public void Stop() {
        server.STOP=true;
        //ServerThread.Join();
#if UNITY_EDITOR
        // Application.Quit() does not work in the editor so
        // UnityEditor.EditorApplication.isPlaying need to be set to false to end the game
        UnityEditor.EditorApplication.isPlaying = false;
#else
         Application.Quit();
#endif
    }
}

//---------------------------------------------------------------------------------------------------------------
//! in Unity: Edit->Project Settings->Player->Settings for PC,Mac & Linux Standalone -> Run In Background  CHECKED
                             