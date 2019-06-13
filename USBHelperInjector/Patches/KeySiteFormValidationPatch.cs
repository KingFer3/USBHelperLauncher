﻿using Harmony;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Reflection.Emit;
using System.Windows.Forms;

namespace USBHelperInjector.Patches
{
    [HarmonyPatch]
    class KeySiteFormValidationPatch
    {
        private static readonly string[] sites = { "wiiu", "3ds" };
        private static string lastError; // Read using reflection

        static MethodBase TargetMethod()
        {
            return ReflectionHelper.FrmAskTicket.OkButtonHandler;
        }

        static bool Prefix(object __instance)
        {
            var textBoxes = ReflectionHelper.FrmAskTicket.TextBoxes.Select(x => (Control)x.GetValue(__instance)).ToArray();
            var textBoxWiiU = textBoxes[0];
            var baseUri = new UriBuilder(textBoxWiiU.Text).Uri;
            var uri = new Uri(baseUri, "json");

            lastError = null;
            using (var client = new HttpClient())
            {
                try
                {
                    var resp = client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead).Result;
                    if (!resp.IsSuccessStatusCode)
                    {
                        lastError = GetCustomHttpErrorMessage((int)resp.StatusCode, resp.ReasonPhrase);
                    }
                }
                catch (HttpRequestException e)
                {
                    lastError = e.Message;
                }
            }

            if (lastError == null)
            {
                // Take the title key site as valid if the request succeeded.
                InjectorService.LauncherService.SetKeySite(sites[0], textBoxWiiU.Text);
                textBoxWiiU.Text = string.Format("{0}.titlekeys", sites[0]);

                // Always give a valid 3DS titlekey url if the Wii U url was valid.
                textBoxes[1].Text = string.Format("{0}.titlekeys", sites[1]);
            }
            else
            {
                // Tell the user the title key site is invalid.
                textBoxWiiU.Text = string.Empty;
                lastError = string.Format("An error occurred while trying to reach {0}:\n\n{1}", baseUri.ToString(), lastError);
            }

            Overrides.ForceKeySiteForm = false;
            return true;
        }

        static IEnumerable<CodeInstruction> Transpiler(MethodBase original, IEnumerable<CodeInstruction> instructions)
        {
            var codes = new List<CodeInstruction>(instructions);

            var showIndex = codes.FindIndex(i => i.opcode == OpCodes.Call && ((MethodInfo)i.operand).Name == "Show");
            if (showIndex == -1)
            {
                return codes;
            }

            var toInsert = new List<CodeInstruction>
            {
                new CodeInstruction(OpCodes.Pop),
                new CodeInstruction(OpCodes.Ldsfld, AccessTools.Field(typeof(KeySiteFormValidationPatch), "lastError"))
            };

            codes.InsertRange(showIndex, toInsert);

            return codes;
        }

        internal static string GetCustomHttpErrorMessage(int statusCode, string statusDescription)
        {
            return string.Format("Remote server replied with status: ({0}) {1}", statusCode, statusDescription);
        }
    }
}