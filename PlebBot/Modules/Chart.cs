﻿using System;
using System.Net.Http;
using System.Threading.Tasks;
using Discord.Commands;
using System.IO;
using System.Linq;
using Discord.WebSocket;
using Npgsql;
using PlebBot.Data.Models;
using PlebBot.Data.Repositories;
using System.Drawing;

namespace PlebBot.Modules
{
    [Group("chart")]
    public class Chart : BaseModule
    {
        private readonly HttpClient httpClient;
        private readonly Repository<User> userRepo;

        public Chart(HttpClient client, Repository<User> repo)
        {
            httpClient = client;
            userRepo = repo;
        }

        [Command("set", RunMode = RunMode.Async)]
        [Summary("Link a chart to your account.")]
        public async Task SetChart(string chartLink = "")
        {
            if (!await Preconditions.Preconditions.InCharposting(Context)) return;

            if (chartLink == String.Empty)
            {
                var temp = Context.Message.Attachments.First().Filename;
                if (temp.EndsWith("png") || temp.EndsWith(".jpg") || temp.EndsWith(".jpeg") || temp.EndsWith(".bmp"))
                    chartLink = Context.Message.Attachments.First().Url;
            }
            var stream = await httpClient.GetByteArrayAsync(chartLink);
            var imgBytes = new byte[stream.GetLongLength(0)];
            stream.CopyTo(imgBytes, 0);

            var condition = $"\"DiscordId\" = {(long) Context.User.Id}";
            try
            {
                await userRepo.UpdateFirst("Chart", imgBytes, condition);
                await Success("Successfully saved the chart.");
            }
            catch (NpgsqlException ex)
            {
                if (Context.Client.GetChannel(417956085253668864) is ISocketMessageChannel dev)
                    await dev.SendMessageAsync($"<@164102776035475458> aaaaaaaaaaaaaa\n{ex.Message}");
            }
        }

        [Command(RunMode = RunMode.Async)]
        [Summary("Send your chart")]
        public async Task SendChart()
        {
            if (!await Preconditions.Preconditions.InCharposting(Context)) return;

            var condition = $"\"DiscordId\" = {(long) Context.User.Id}";
            var user = await userRepo.FindFirst(condition);
            if (user.Chart == null)
            {
                await Error("You haven't saved a chart to your profile.");
                return;
            }
            var img = new MemoryStream(user.Chart);
            await Context.Channel.SendFileAsync(
                img, $"{Context.User.Username}_chart.png",
                $"{Context.User.Mention}'s chart:");
        }

        [Group("top")]
        public class TopCharts : Chart
        {
            public TopCharts(HttpClient client, Repository<User> repo) : base(client, repo)
            {
            }

            //[Command("weekly")]
            //[Alias("week")]
            //public async Task TopWeek()
        }
    }
}