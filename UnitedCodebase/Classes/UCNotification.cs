using Microsoft.Toolkit.Uwp.Notifications;
using System;
using Windows.UI.Notifications;

namespace UnitedCodebase.Classes
{
    public class UCNotification
    {
        public string ToastButtonContent;
        public string ToastButtonArguments;
        public string ToastButton2Content;
        public string ToastButton2Arguments;
        public string ToastLaunchArguments;
        public string Title;
        public string Content;
        public DateTimeOffset? Time;
        public ToastAudio Audio;
        public ToastDuration? ShowDuration;


        public UCNotification(string _title, string _content, DateTimeOffset? _time = null, ToastAudio _audio = null, ToastDuration? _showDuration = null)
        {
            Title = _title;
            Content = _content;
            Time = _time;
            Audio = _audio;
            ShowDuration = _showDuration;
        }

        public ToastNotification Toast
        {
            get
            {
                // Construct the visuals of the toast
                ToastVisual visual = new ToastVisual()
                {
                    BindingGeneric = new ToastBindingGeneric()
                    {
                        Children =
                        {
                            new AdaptiveText()
                            {
                                Text = Title
                            },

                            new AdaptiveText()
                            {
                                Text = Content
                            },
                        },
                    }
                };

                // Now we can construct the final toast content
                ToastContent toastContent = new ToastContent()
                {
                    Visual = visual
                };

                if (Audio != null)
                {
                    toastContent.Audio = Audio;
                }

                if(ShowDuration != null)
                {
                    toastContent.Duration = ShowDuration.Value;
                }

                if (ToastLaunchArguments != null)
                {
                    toastContent.Launch = ToastLaunchArguments;
                }

                ToastActionsCustom actionsCustom = new ToastActionsCustom();

                if (ToastButtonContent != null && ToastButtonArguments != null)
                {
                    actionsCustom.Buttons.Add(new ToastButton(ToastButtonContent, ToastButtonArguments)
                    {
                        ActivationType = ToastActivationType.Foreground
                    });
                }

                if (ToastButton2Content != null && ToastButton2Arguments != null)
                {
                    actionsCustom.Buttons.Add(new ToastButton(ToastButton2Content, ToastButton2Arguments)
                    {
                        ActivationType = ToastActivationType.Foreground
                    });
                }

                toastContent.Actions = actionsCustom;

                // And create the toast notification
                ToastNotification _toast = new ToastNotification(toastContent.GetXml());

                if (Time != null)
                {
                    _toast.ExpirationTime = Time;
                }


                return _toast;
            }
        }

        public void ShowNotification()
        {
            ToastNotificationManager.CreateToastNotifier().Show(Toast);
        }
    }
}

