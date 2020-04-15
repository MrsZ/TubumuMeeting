﻿using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace TubumuMeeting.Meeting.Authorization
{
    /// <summary>
    /// 签名帮助类
    /// </summary>
    public static class SignatureHelper
    {
        /// <summary>
        /// 生成签名凭据
        /// </summary>
        /// <param name="secretKey"></param>
        /// <returns></returns>
        public static SigningCredentials GenerateSigningCredentials(string secretKey)
        {
            var signingKey = GenerateSigningKey(secretKey);
            var signingCredentials = new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256);

            return signingCredentials;
        }

        /// <summary>
        /// 生成签名 Key
        /// </summary>
        /// <param name="secretKey"></param>
        /// <returns></returns>
        public static SymmetricSecurityKey GenerateSigningKey(string secretKey)
        {
            var keyByteArray = Encoding.UTF8.GetBytes(secretKey);
            var signingKey = new SymmetricSecurityKey(keyByteArray);

            return signingKey;
        }
    }
}
