﻿using CLI.WebAppServices.Middleware;
using Core.LogModule;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net.Mime;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using static CLI.WebAppServices.Middleware.InterfaceAuthentication;

namespace CLI.WebAppServices.Api
{
    /// <summary>
    /// 修改自动录制设置
    /// </summary>
    [Produces(MediaTypeNames.Application.Json)]
    [ApiController]
    [Route("api/set_rooms/[controller]")]
    [Login]
    public class modify_recording_settings : ControllerBase
    {
        /// <summary>
        /// 批量修改房间的录制设置
        /// </summary>
        /// <param name="uid">要修改录制状态的房间UID列表</param>
        /// <param name="state">将房间的录制状态设置为什么状态</param>
        /// <param name="commonParameters"></param>
        /// <returns></returns>
        [HttpPost(Name = "modify_recording_settings")]
        public ActionResult Post(List<long> uid, bool state, PostCommonParameters commonParameters)
        {
            List<long> count = Core.RuntimeObject._Room.ModifyRecordingSettings(uid, state);
            return Content(MessageBase.Success(nameof(modify_recording_settings),count,$"返回列表中的房间的自动录制修改为{state}"), "application/json");
        }
    }

    /// <summary>
    /// 修改开播提示设置
    /// </summary>
    [Produces(MediaTypeNames.Application.Json)]
    [ApiController]
    [Route("api/set_rooms/[controller]")]
    [Login]
    public class modify_room_prompt_settings : ControllerBase
    {
        /// <summary>
        /// 批量修改房间的开播提醒设置
        /// </summary>
        /// <param name="uid">要修改开播提示提示状态的房间UID列表</param>
        /// <param name="state">将房间的开播提示状态设置为什么状态</param>
        /// <param name="commonParameters"></param>
        /// <returns></returns>
        [HttpPost(Name = "modify_room_prompt_settings")]
        public ActionResult Post([FromForm] List<long> uid, [FromForm] bool state, PostCommonParameters commonParameters)
        {
            List<long> count = Core.RuntimeObject._Room.ModifyRoomPromptSettings(uid, state);
            return Content(MessageBase.Success(nameof(modify_room_prompt_settings), count, $"返回列表中的房间的开播提示修改为{state}"), "application/json");
        }
    }


    [Produces(MediaTypeNames.Application.Json)]
    [ApiController]
    [Route("api/set_rooms/[controller]")]
    [Login]
    public class add_room : ControllerBase
    {
        /// <summary>
        /// 添加房间
        /// </summary>
        /// <param name="commonParameters"></param>
        /// <param name="auto_rec">是否自动录制</param>
        /// <param name="remind">是否开播提示</param>
        /// <param name="rec_danmu">是否录制弹幕</param>
        /// <param name="uid"></param>
        /// <param name="room_id"></param>
        /// <returns></returns>
        [HttpPost(Name = "add_room")]
        public ActionResult Post(PostCommonParameters commonParameters, [FromForm] bool auto_rec, [FromForm] bool remind, [FromForm] bool rec_danmu, [FromForm] long uid = 0, [FromForm] long room_id = 0)
        {
            var addInfo = Core.RuntimeObject._Room.AddRoom(auto_rec, remind, rec_danmu, uid, room_id);
            return Content(MessageBase.Success(nameof(modify_room_prompt_settings), addInfo.State, $"{addInfo.Message}"), "application/json");
        }
    }
}
