﻿using OCUnion;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Transfer;
using Util;
using Model;
using OCUnion.Transfer.Model;

namespace Transfer
{
    public class SessionClient
    {
        public const int DefaultPort = 19019; // :) https://www.random.org/integers/?num=1&min=5001&max=49151&col=5&base=10&format=html&rnd=new
        public const bool UseCryptoKeys = false;
        private Object LockObj = new Object();
        #region

        public volatile bool IsLogined = false;

        public ConnectClient Client;
        private byte[] Key;
        public string ErrorMessage;

        public void Disconnect()
        {
            try
            {
                IsLogined = false;
                if (Client != null) Client.Dispose();
            }
            catch
            {
            }

            Client = null;
        }

        public bool Connect(string addr, int port = 0)
        {
            ErrorMessage = null;
            if (port == 0) port = DefaultPort;
            try
            {
                IsLogined = false;
                if (Client != null) Client.Dispose();
            }
            catch
            { }

            try
            {
                //Loger.Log("Client Connect1");
                // Generate open-close keys  KClose-KOpen
                //Генерим рандомную пару КЗакр-КОткр
                var crypto = new CryptoProvider();
                if (UseCryptoKeys) crypto.GenerateKeys();

                //Loger.Log("Client Connect2");
                Client = new ConnectClient(addr, port);

                //Loger.Log("Client Connect3");//Строго первый пакет: Передаем серверу КОткр
                if (UseCryptoKeys)
                    Client.SendMessage(Encoding.UTF8.GetBytes(crypto.OpenKey));
                else
                    Client.SendMessage(new byte[1] { 0x00 });

                //Loger.Log("Client Connect4");
                //Строго первый ответ: Передаем клиенту КОткр(Сессия)
                var rc = Client.ReceiveBytes();
                if (UseCryptoKeys)
                    Key = crypto.Decrypt(rc);
                else
                    Key = rc;

                //Loger.Log("Client Connect5");
                //Обмен по протоколу ниже: Передаем серверу Сессия(Логин - Пароль или запрос на создание)

                //Запускаем таймер фоново поддерживающий открытое соединение при простое
                ConnectSaver.AddClient(Client, (cl) =>
                {
                    lock (LockObj)
                    {
                        cl.SendMessage(new byte[1] { 0x00 });
                        cl.ReceiveBytes();
                    }
                });

                return true;
            }
            catch (Exception e)
            {
                ErrorMessage = e.Message
                    + (e.InnerException == null ? "" : " -> " + e.InnerException.Message);
                ExceptionUtil.ExceptionLog(e, "Client");
                return false;
            }
        }

        /// <summary>
        /// Пинг
        /// </summary>
        /// <returns></returns>
        public bool ServicePing()
        {
            try
            {
                lock (LockObj)
                {
                    ErrorMessage = null;
                    Client.SendMessage(new byte[1] { 0x00 });

                    var rec = Client.ReceiveBytes();

                    return rec.Length == 1 && rec[0] == 0x00;
                }
            }
            catch (Exception e)
            {
                ErrorMessage = e.Message
                    + (e.InnerException == null ? "" : " -> " + e.InnerException.Message);
                ExceptionUtil.ExceptionLog(e, "Client ServicePing ");
                return false;
            }
        }

        /// <summary>
        /// Проверка есть ли новое на сервере, используется только для чата 
        /// </summary>
        /// <returns></returns>
        public bool? ServiceCheck()
        {
            try
            {
                lock (LockObj)
                {
                    ErrorMessage = null;
                    Client.SendMessage(new byte[1] { 0x01 });

                    var rec = Client.ReceiveBytes();

                    return rec.Length == 1 && rec[0] == 0x01;
                }
            }
            catch (Exception e)
            {
                ErrorMessage = e.Message
                    + (e.InnerException == null ? "" : " -> " + e.InnerException.Message);
                ExceptionUtil.ExceptionLog(e, "Client ServiceCheck ");
                return null;
            }
        }

        /// <summary>
        /// Передаем и принимаем объект ModelContainer
        /// </summary>
        private ModelContainer Trans(ModelContainer sendObj)
        {
            lock (LockObj)
            {
                ErrorMessage = null;

                var time1 = DateTime.UtcNow;

                var ob = GZip.ZipObjByte(sendObj); //Serialize
                var send = CryptoProvider.SymmetricEncrypt(ob, Key);

                if (send.Length > 1024 * 512) Loger.Log($"Client Network toS {send.Length} unzip {GZip.LastSizeObj} ");
                var time2 = DateTime.UtcNow;

                Client.SendMessage(send);

                var time3 = DateTime.UtcNow;

                var rec = Client.ReceiveBytes();

                var time4 = DateTime.UtcNow;

                var rec2 = CryptoProvider.SymmetricDecrypt(rec, Key);

                var time5 = DateTime.UtcNow;

                var res = (ModelContainer)GZip.UnzipObjByte(rec2); //Deserialize

                var time6 = DateTime.UtcNow;
                if (rec.Length > 1024 * 512) Loger.Log($"Client Network fromS {rec.Length} unzip {GZip.LastSizeObj} ");

                if ((time5 - time1).TotalMilliseconds > 900)
                {
                    Loger.Log($"Client Network timeSerialize {(time2 - time1).TotalMilliseconds}" +
                        $" timeSend {(time3 - time2).TotalMilliseconds}" +
                        $" timeReceive {(time4 - time3).TotalMilliseconds}" +
                        $" timeDecrypt {(time5 - time4).TotalMilliseconds}" +
                        $" timeDeserialize {(time6 - time5).TotalMilliseconds}");
                }

                return res;
            }
        }

        /// <summary>
        /// Передаем объект с указанием номера типа.
        /// </summary>
        protected T TransObject<T>(object objOut, int typeOut, int typeIn)
            where T : class
        {
            //Loger.Log("Client T2");
            try
            {
                var pack = new ModelContainer()
                {
                    TypePacket = typeOut,
                    Packet = objOut
                };
                var res = Trans(pack);
                var stat = res.Packet as T;
                if (res.TypePacket != typeIn
                    || stat == null)
                    throw new ApplicationException($"Unknow server error TransObject({typeOut} -> {typeIn}) responce: {res.TypePacket} "
                        + (res.Packet == null ? "null" : res.Packet.GetType().Name));
                return stat;
            }
            catch (Exception e)
            {
                ErrorMessage = e.Message
                    + (e.InnerException == null ? "" : " -> " + e.InnerException.Message);
                ExceptionUtil.ExceptionLog(e, "Client");
                return null;
            }
        }

        public T TransObject2<T>(object objOut, PackageType typeOut, PackageType typeIn)
            where T : class
        {
            return TransObject<T>(objOut, (int)typeOut, (int)typeIn);
        }

        /// <summary>
        /// Передаем объект с указанием номера типа.
        /// Ответ должен придти как ModelStatus со статусов == 0 и указанным номером типа.
        /// </summary>
        private bool TransStatus(object objOut, int typeOut, int typeIn)
        {
            var stat = TransObject<ModelStatus>(objOut, typeOut, typeIn);

            if (stat != null && stat.Status != 0)
            {
                ErrorMessage = stat.Message;
                return false;
            }
            return stat != null;
        }
        #endregion

        /*
1 - регистрация (логин, пароль)
2 - ответ регистрации (успешно, сообщение)
3 - вход (логин, пароль)
4 - ответ на вход (успешно, сообщение)
5 - запрос информации 
6 - информация о самом пользователе
7 - создать мир (всё, что нужно для начала работы сервера)
8 - ответ на 7 (успешно, сообщение)
 9 - созадть поселение (запрос с данными о поселении нового игрока, всё что передается после создания карты поселения игроком)
 10 - ответ на 9 (успешно, сообщение)
11 - синхронизация мира (тип синхранезации, время последней синхронизации, все данные для сервера)
12 - ответ на 11 (время сервера, все данные мира которые изменились с указанного времени)
 13 - создать игру (id лобби)
 14 - ответ (сиид для создания мира, ?)
15 - отправка игрового действия (данные для обновления на сервере)
16 - ответ (успешно, сообщение)
17 - обновить чат (время после которого нужны данные)
18 - данные чата
19 - написать в чат (id канала, сообщение) //здесь же командами создать канал, добавить в канал и прочее
20 - ответ (успешно, сообщение)
21 - команды работы с биржей
22 - ответ 
23 - команды работы с биржей
24 - ответ 
25 - команды работы с биржей
26 - ответ 
27 - атака онлайн
28 - ответ  
29 - атакуемый онлайн
30 - ответ 
        */

        public bool Registration(string login, string pass)
        {
            var packet = new ModelLogin() { Login = login, Pass = pass };
            var good = TransStatus(packet, 1, 2);

            if (good) IsLogined = true;
            return good;
        }

        public bool Login(string login, string pass)
        {
            var packet = new ModelLogin() { Login = login, Pass = pass };
            var good = TransStatus(packet, 3, 4);

            if (good) IsLogined = true;
            return good;
        }

        public bool Reconnect(string login, string key)
        {
            var packet = new ModelLogin() { Login = login, KeyReconnect = key };
            var good = TransStatus(packet, 3, 4);

            if (good) IsLogined = true;
            return good;
        }

        public ModelInfo GetInfo(OCUnion.Transfer.ServerInfoType serverInfoType)
        {
            Loger.Log("Client GetInfo " + serverInfoType.ToString());
            var packet = new ModelInt() { Value = (int)serverInfoType };
            var stat = TransObject<ModelInfo>(packet, 5, 6);
            return stat;
        }

        public ModelPlayToClient PlayInfo(ModelPlayToServer info)
        {
            //Loger.Log("Client PlayInfo "/* + info.TypeInfo.ToString()*/);
            var stat = TransObject<ModelPlayToClient>(info, 11, 12);
            return stat;
        }

        public ModelUpdateChat UpdateChat(ModelUpdateTime modelUpdate)
        {
            Loger.Log("Client UpdateChat " + modelUpdate.Time.ToGoodUtcString());
            var packet = modelUpdate;
            var stat = TransObject<ModelUpdateChat>(packet, 17, 18);
    
            return stat;
        }

        public ModelStatus PostingChat(int chatId, string msg)
        {
            Loger.Log("Client PostingChat " + chatId.ToString() + ", " + msg);
            var packet = new ModelPostingChat() { IdChat = chatId, Message = msg };
            var stat = TransObject<ModelStatus>(packet, 19, 20);

            ErrorMessage = stat?.Message;

            return stat;
        }

        public Player GetPlayerByToken(Guid guidToken)
        {
            var stat = TransObject2<Player>(guidToken, PackageType.RequestPlayerByToken, PackageType.ResponsePlayerByToken);

            return stat;
        }
    }
}