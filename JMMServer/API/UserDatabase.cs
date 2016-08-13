﻿using System;
using System.Collections.Generic;
using System.Linq;

namespace JMMServer.API
{
    /// <summary>
    /// UserDatabase is class that help with auth user
    /// </summary>
    public class UserDatabase
    {
        //ActiveApiKeys: userid, device, apikey
        static readonly List<Tuple<int, string, string>> ActiveApiKeys = new List<Tuple<int, string, string>>();
        //Users: userid, username, password
        private static readonly List<Tuple<int, string, string>> Users = new List<Tuple<int, string, string>>();

        static UserDatabase()
        {
            Repositories.JMMUserRepository repUsers = new Repositories.JMMUserRepository();
            foreach (Entities.JMMUser us in repUsers.GetAll())
            {
                Users.Add(new Tuple<int, string, string>(us.JMMUserID, us.Username, us.Password));
            }

            Repositories.AuthTokensRepository authRepo = new Repositories.AuthTokensRepository();
            foreach (Entities.AuthTokens at in authRepo.GetAll())
            {
                ActiveApiKeys.Add(new Tuple<int, string, string>(at.UserID, at.DeviceName, at.Token));
            }
        }

        public static Entities.JMMUser GetUserFromApiKey(string apiKey)
        {
            var activeKey = ActiveApiKeys.FirstOrDefault(x => x.Item3 == apiKey);

            if (activeKey == null)
            {
                return null;
            }

            var userRecord = Users.First(u => u.Item1 == activeKey.Item1);
            return new Entities.JMMUser(userRecord.Item2);
        }

        public static string ValidateUser(string username, string password, string device)
        {
            var userRecord = Users.FirstOrDefault(u => u.Item2.ToLower() == username.ToLower() && u.Item3 == password);

            if (userRecord == null)
            {
                return null;
            }

            int uid = new Entities.JMMUser(username).JMMUserID;
            string apiKey = "";
            try
            {
                var apiKeys = ActiveApiKeys.First(u => u.Item1 == uid && u.Item2 == device.ToLower());
                apiKey = apiKeys.Item3;
            }
            catch
            {
                apiKey = Guid.NewGuid().ToString();
                ActiveApiKeys.Add(new Tuple<int, string, string>(uid, device.ToLower(), apiKey));
            }

            return apiKey;
        }

        public static bool RemoveApiKey(string apiKey)
        {
            try
            {
                var apiKeyToRemove = ActiveApiKeys.First(x => x.Item3 == apiKey);
                //remove auth from repository/database
                Repositories.AuthTokensRepository authRepo = new Repositories.AuthTokensRepository();
                authRepo.Delete(apiKeyToRemove.Item1);
                //remove from memory
                ActiveApiKeys.Remove(apiKeyToRemove);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}