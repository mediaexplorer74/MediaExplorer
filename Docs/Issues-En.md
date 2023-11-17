I really can't watch it anymore, that's why I posted this article, code owner, take a look #122
Closed
ugksoft opened this issue on Jul 17, 2018 · 71 comments
Comments
@ugksoft
ugksoft commented on Jul 17, 2018 •
Mainly because I can't watch it anymore, I just wrote it. Some of them are too familiar. I pull these common sense, but they show me as a child.,
Maybe you also understand, it's just because you're lazy that you don't do that. It's not your dish.,
In order to avoid misunderstandings, quarrels, etc., please explain

You are the default cdecl for all DLLs!!! , In Windows, the standard rule is stdcall!!!!!!!, Including the callback is stdcall,
Now I'm going to change cdecl. Other people's programs will inevitably report errors. Look at your number of users. If the amount is small, you can guarantee that the notification is received, or if the problem is not large, then fix it to stdcall, and consider it as appropriate.
DWORD __stdcall GetTickCount();
DWORD __stdcall Sleep(dwMilliseconds:DWORD);
There are four common types of stdcall/pascal/fastcall/cdecl. In general, the order in which the parameter registers of the API are stored is from left to right and from right to left.,
Who releases the temporary stack, stdcall is from right to left (the purpose is format (xxx, a, b.c...) When the length is variable..
Inside VC, or the default cdecll, when no modifiers are added, the default is also cdecll

I found it for you, you can take a look
https://www.linuxidc.com/Linux/2010-04/25290.htm
http://www.3scard.com/index.php?m=blog&f=view&id=10
(There is no BS you mean, pure kindness, in your code, you did not notice the mandatory stdcall, maybe this instant noodle awareness is weak, so I just emphasized it)

For the first time in my life, I saw someone writing a dll, and the string that uses an internal class as a callback parameter is a C++ built-in type. In fact, it can be regarded as a class (the class is actually compiled, and it is generally processed into a record/struct form and stored in the compiled bin)
Different versions and different compilers (vc, bcb, or other c++ compilers) have their own processing methods for this kind of non-direct memory data.
Unless you force the user to use the same version of vc as you (if Microsoft patching this vc involves string, then everyone even needs to patch the same), or the dll and the exe are compiled by the user's own compiler, or the code of the dll is directly included in the exe and the dll is not used anymore. Otherwise, the data in memory must be different, and it is impossible for the exe to call the dll normally. At that time, there will be various irrational header problems, and the reason cannot be found.
Correction method, all changed to char* (you can consider wchar, but generally char* is the main one)
As a dll, it needs to be standardized according to Windows standards, so all compilers can support it well.
Common delphi7/DelphiXE, VC, BorandC++, Java, gcc (MinGW/Cygwin)...
This specification is simply put,
A.stdcall (individual reasons for efficiency, or convenience of development, etc., if you do not follow the above, you must also specify in writing)
B. When the API exchanges data, 32bit alignment is required, and some enumerations, bool or others must be explicitly declared to prevent different compilation parameters from affecting memory alignment.
The parameters should be as memory-based as possible. Generally, * AnsiChar is the main one. Individual parameters can be * WideChar, but most of them are * ansichar.
This is compatible with utf8 and gbk
WideChar, there are only 65,535 characters at most, but not so many. When processing new characters, it will inevitably be lossy, so widechar should not be used.
Because when windows was originally designed, in the 1990s, thinking that 65535 was enough, now there is a pile of garbage in utf8, maybe one day 4byte
It may not be enough, so ansichar/utf8/gbk is the main one!!!, At least it won't be wrong
C.For all enumerations, you need to force the start and end points to be specified, that is, enum_start=0, enum_MAX=0x7fffffff,
In this way, avoid the compiler and make your own initiative. If it doesn't exceed 255 at first glance, use a byte to store it. It's okay in a program to exchange data across files.
It's a problem
D.For all structures, the alignment must be forcibly specified. Usually aligned in units of 4byte,

#pragma pack(4) //Force 4byte alignment
struct
{
//DWORD dwsize for complex data, consider adding dwsize so that the dll can determine which SDK version the exe was developed based on dwsize.,
// At the same time, it can also be used for out-of-bounds judgment during operation. Most windows structures have such sizes and values.,
char ver;
char flag;
char res1[2]; 2 are missing here, so 2 are deliberately added (can also be used for future expansion function reservation),
To prevent the compiler from shrinking, always fill in 0 when not in use,
When you want to use it in the future, know that 0 is not used, and non-0 is valid data.
char X;
char Y;
char X;
char res2[1]; At the end, prevent the compiler from shrinking, so it is deliberately aligned
}
#pragma pack()//Restore the compiler's default alignment

In this way, the res member is deliberately added to occupy the position. Under normal circumstances, the 32-bit system is normal.,
In case the 64bit compiler, the compiler developer will not be able to think about it for a while in the future. 64bit defaults to 8byte alignment, or the compiler needs to set the parameters to 8byte alignment because of the need for other data. At this time, an error will inevitably occur.
So it's best to add pragma pack to specify it explicitly

After all these size values are set, due to the large number of structure members, they may be mistaken, for example, res[xx] is miscalculated, etc.
After all are done, you need to printf("Name=%s, sizeof=%d", xxx sizeof(xxx)); click, confirm all one by one

However, in general, you only need to modify the h file. The amount of changes is not large. It's just that your old users may have compatibility problems.
(They need to take this header file and recompile their program. There is no need to change a line in the code)

Some functions, such as wkeGetUserAgent, return a pointer to ansichar,
Normally, it should be that the user is given a memory space, and the dll is responsible for filling in the data, usually it will be called twice.
When called for the first time, buff=null,*size returns the size
The second time, apply for the memory size according to the size, and then get (buff, size) If it is string data, to prevent accidents and compatibility, usually size+1 applies for one more data,
(Or the user knows that this data is 4K long and it is dead, so he directly calls it once and gives 4K memory. After the DLL is called, the size is updated to report the effective length.)
In this way, users don't have to doubt whether the returned memory block needs to be released by an external exe.
No matter what language, the default is who calls and who applies for memory (exe calls API_Filldata (xxx), then xxx is applied by exe and released by exe,)
However, the diagram is convenient, and the char* data can be returned directly in a rogue format. Most of them will not go wrong, but:
char*p=GetCurUrl; //返回www.xxx.com
int Len=strlen(p);
A bunch of follow-up operations by the user may waste a lot of time, it may also be that the user's computer is too stuck or other reasons, and at the same time the web thread, after js counts down, points to www.newurl.com
CPU time slices, when rotating between code blocks, it is impossible to guarantee that everyone is equal. Some have more rounds and some have less. Under multi-core, the advantages are obvious. When the process is stuck, the advantages are obvious.
The user then calls p, then the value of p has become newurl, it may be released by you, or it may not be safe (end problem)
for (int i=0... i<Len)
{
.....
}
In order to solve this problem, it is necessary to state in writing and tell the user that the result value is safe, constant, and yes...Wait a minute
But in the end it is not the normal way to call
The user has no choice but to solve the problem. It may be that after GetCurURL, the newmem block of memory immediately copies the results to prevent the web core from changing the data, but it is not as good as the user's own application. The solution is appropriate.

The original author of WKE, I don't know if it's you, it may not be the original author of WKE deliberately, GG is more likely
wke.In the dll, drawing the screen actually requires the user to implement it by himself, apply for the bmp area by himself, calculate the mouse position by himself, etc., which is super perverted, a waste of memory, and a waste of IO. This approach, as far as I know, only andriod's api has a display structure like this.,
Most users don't think about this, they just want to call IE's Webbrower as simply as IE's Webbrower, because IE's webbroiwer compatibility is getting worse and worse. Many times when a Web page is displayed, a js error will be reported, or an item cannot be found or the like (but running in standard IE does not have this problem)
Others, if they want to implement some high-level and advanced functions, they will hand over the screen-drawing function to the user at this time. The user may want to embed a super autonomous web page in the exe, or even in the game, it may be possible to display a Web page in the texture mode. At this time, raw data such as GetBmp and GetYUV are required.
Your wkeCreateWebWindow is much more convenient than the original WKE. I think the original WKE is unreasonable, so I mentioned this.
5.Your DLL exported C++ classes, a bunch of them??xxxxxxxx@!$#@!$@#$!The export name is actually not safe, and the development is not convenient.
A good practice is to imitate COM and export a process COM interface (such as wmp.dll is like this)
For example, define a
WKECRETEWEBCORE(); Return interface
The interface is as follows:
Interface Ixxxxxxx;
function setpos(L,T,W,H,isshow);
function LoadUrl(url )
function ....
....
(For details, you can open an example casually in vc, or use exescope to open c:\windows\system32\wmp.dll

When users are using it, they don't need to create any classes, they just need to
web=wkeCreateWebCore();
web.setpos(...)
web.loadurl(...)
function regcallback(CB *callback,void *user);
This is exactly the same as the standard class code

However, when the com method has shortcomings, there are some places where the user does not know the essence, such as the pointer of the pointer, and whether the memory should be released in the end (for example, DirecShow->mediatype.Format data), call order, etc., it's a bit confusing, so,
When using it, try to follow the standard, original data type as much as possible. When there may be memory release problems, write it in text.
When dealing with callbacks and event classes, don't use the COM method, but the standard C/C++ method.

Do not use COM other than the process (for example, when booting, the start menu button is stuck, or when some programs are operating, the start menu is stuck, IE is stuck, operate it, the result will be affected by the resource manager, etc.,.It's all cross-process COM, it's not recommended to do this)

Too much.I don't want to say it anymore, it's not clear in one sentence or two.,

@weolar
Owner
weolar commented on Jul 17, 2018 •
Typical don't pretend to understand.After learning c++ for half a year, I came out and shook it.
1. Whether to use cdecl or stdcall is my own business.The caller's declaration is followed by the same declaration.The people who make mistakes are all people who don't read the header files.I am not an official dll of Microsoft, what do you care about my use?So many large open source projects in the world, especially pure C, are basically cdecl, such as curl.
2. What to say about using internal classes, it means that first you haven't read the mb code, and second, you write less code.The wkeString I exported is just a pointer, and if you need to operate, you can only call the provided api such as wkeGetString.What internal class do you care about me exporting?Look at the header file of Microsoft, is HWND also the internal class you mentioned?
3. Align the structure with this, just follow the default settings of Vs.Other compilers are set to the default alignment of Vs.I will state this in the document next time.It's not a mistake, what are you excited about?
4. These days, there are still people who use memory to transfer data twice. I am also drunk.This is the stupidest design.Others just need a few strings, and you have to figure out such a complicated way to get it.If you need to get 2 strings, you have to new four times and 2 loops, which is troublesome.Now if the string returned by mb is const char*, it means that the next frame is recovered.You don't need to recycle it, you can't save it as a persistent string, and there is no use until half of the content is changed.Just look at the source code for this.Of course, it's also my fault that I didn't write the document, so I'll add it later.
5. Export C++ classes, a bunch of them??xxxxxxxx@!$#@!$@#$!。Did I let you use these?These classes are not mentioned in my documentation and header files at all. This is for internal use in my electron mode.What do you care about me?Did I ask someone to call
6，“wke.In dll, painting the screen actually requires the user to implement it by himself, apply for the bmp area by himself, calculate the mouse position by himself, etc., Super perverted, waste memory, waste IO,” This is called off-screen rendering, it is for places that need to be painted in games (for example, D3D requires bitmap).Although cumbersome, it can be controlled in more detail.If you don't need it, use a simple wkeCreateWebWindow.Both methods have uses, and there is no question of right or wrong.What are you excited about?
7, you actually want me to export com.What age is this, who still wants to use such a cumbersome export method.Now it is unified into a pure C interface, is there a problem?

@1ocalhost
1ocalhost commented on Jul 17, 2018
The landlord's text is too emotional, childish, and emotional. I don't have the patience to finish reading it anyway. I like it too much. It's stinky and long.。。 @weolar sweeps the floor, just close these issues directly️️

@ugksoft
Author
ugksoft commented on Jul 17, 2018
Saying that others don't understand and pretend to understand, I have been touching the computer for 20 years. I wanted to use it, but found that it was getting more and more chaotic. It took a few hours to translate your header file, and finally called it, and reported an error. I hate it. My teeth are itchy, that's why I have this article (I use Delphi7)

It's your business to use cdecl or stdcll, so you can put this on github. The purpose is for everyone to use it. Naturally, it has to be according to the standard.
Even if you like cdcell, then you can at least write a sentence explicitly. Generally, secondary development documents, SDKs, etc., are all stdcall.
Because the standard is stdcall, I have encountered some situations where I first used cdcel and then secretly changed it to stdcall. That's why I mentioned it.

When is HWND a class? Obviously it's a DWORD, wkestring is internal to you, and let the user adjust wkeGetString. Isn't this troublesome? Anyway, if the user registers for the callback, he must use this data, and let the user adjust wkeGetString one more time.

3.The structure is aligned, everyone knows that it is set by default, but do you confirm that the user must be a VS user?, The user's program is definitely a pure Web application?For complex applications, the definition is usually messy. It's better to specify it explicitly. You can't be wrong about your own dll/exe, but it's definitely a mistake across users and languages.

4.Explicit call, this is in the case where the memory size is not clear (for complex applications, when the structure is messy, the data may be oversized or small)
But for general applications, when everyone is clear, just apply for enough memory directly, so that the memory area inside the module will not be exposed to the outside.
Suppose void*v = getData(); There is too much code, the user does not pay attention, and if he writes to v, he will die (const just tells the compiler to check the syntax at compile time, not really read-only)
You recycle it yourself, but the user doesn't know it. It's not safe in a multithreaded environment.

5.??XXX??Xx, generally developers don't write directly, and they will use it when they cross languages (I don't use it). I'm just saying that this is not very good and should be avoided. Your users shouldn't use extreme situations such as gcc, so don't consider it.

Why do you keep talking about excitement? Although this kind of rendering solves many problems simply, it takes too much effort. I'm just saying that the original wke method is not very good.
Most people don't need it. They recognize and praise you for having wkeCreateWebWindow. Do you still want to say that I am bad?

Exporting com is just a lazy way to export classes. I don't like com very much. I just think of it so messy. I'll give you an idea.

In addition, I
wkeInit;
wkeCreateWebWindow;
wkeResize;
(...Some registration, optional or not, all the same)
wkeLoadURL(htttp://aaa.com/phpinfo.php)
wkeLoadURL('http://github.com/weolar/miniblink49');
Static and simple pages, all OK
but
wkeLoadURL('www.163.com ') Report a division by 0 error "floating point division by zero"
Suspecting that it is the cause of the pop-up window, go to register his pop-up callback, but there is no callback, so this reason is ruled out.

For what reason?

Forget it, I don't want to say it anymore, you said I don't pretend to understand,
If you have accumulated so much experience, it will naturally be super messy. You have to talk about all aspects, but if you have too much, it will make people feel unclear.

Then I said you, a typical child, just came out to mix and have no experience. It's just that you are a young person with good energy, so you will tinker with a code to put on github or something.
(The last half of the sentence, semantically neutral,)

I don't want to say it anymore.88 close

@ugksoft ugksoft closed this as completed on Jul 17, 2018
@1ocalhost
1ocalhost commented on Jul 17, 2018
Oh, good boy, @ugksoft
download

@weolar
Owner
weolar commented on Jul 17, 2018 •
1. When is stdcall the standard?What is the standard?Microsoft's use of stdcall is the standard?Does Microsoft have a document stipulating that all DLLs running on his machine must have stdcall?My wke.h explicitly wrote #define WKE_CALL_TYPE cdecl, all export functions are cdecl, is there a problem?
2. HWND is typedef struct HWND *HWND in Microsoft's implementation; as for your external use, it can be used as a DWORD.The wkeString I exported is also used in the same way.
3. Byte alignment, I will write this clearly in the next version.
4，??XXX??Xx This kind is for my own use.And those things are all v8 things, which are actually for people who call nodejs.Of course it can only be used in this form.If you want to spray, just spray nodejs.
5. If you say there is an error, then check whether the calling method and the calling parameters are correct.If it's okay, you can feedback bugs to me.But I believe such a simple call will not be a problem for mb.

@1ocalhost
1ocalhost commented on Jul 17, 2018
Oh, good boy, sweep the floor, @weolar
cute

@weolar
Owner
weolar commented on Jul 17, 2018
Okay, everyone, calm down.At present, there are two problems you mentioned. It is true that the mb document and header file are not clearly written.One is that the byte alignment is not specified, but the byte size of the enumeration.Add an explicit declaration to the next version

@weolar
Owner
weolar commented on Jul 17, 2018
Except for byte alignment and enumeration issues, other spray points are not accepted

@yangyxd
yangyxd commented on Jul 17, 2018
Don't spray indiscriminately, and you have been touching the computer for 20 years. You can't figure it out. It's okay to say it.
Unconditionally support the monks.

@ugksoft ugksoft reopened this on Jul 17, 2018
@ugksoft
Author
ugksoft commented on Jul 17, 2018
Reply to the previous few people after reopen

In my above content, I may unknowingly have the meaning of spraying, but the content is very, very, very small, mainly because I found that the author did not comply with it.
Still worried about misunderstandings, I deliberately added a sentence, don't quarrel

The rules of the rivers and lakes of some DLLs are not standardized, so let's publish them. The purpose is to hope that the author will pay attention to them next time. It can't be changed now, but write a definition or a document to explain it.,
Maintaining a code is actually a lot of work. I don't blame the author, including laziness, inexperience, mistakes, or anything else. There is no meaning to blame.

However, the author's self-esteem is bursting, and he bullies people in turn.

I have been touching computers for 20 years. It has nothing to do with being able to handle it. It's just that I have a lot of experience, mixed flavors, and many old-fashioned words.,
Or those who don't say that they are not likely to make novice mistakes, so they can consider each other's problems and focus on simple but unpopular issues, or deep possibilities.
The purpose of DLL is
1.When multiple programs are shared, there is only one piece in memory, which saves memory.When multiple processes share, there is only one kernel, just map it
2.Modular, multi-user independent division of labor development, it doesn't matter how others implement it, just do your own thing
After the DLL is made, the caller does not need to understand the internal situation, and can toss it casually. He does not know the real address of something inside the DLL. All the values provided by the DLL will not destroy the DLL space and the code is safe.,

If it takes N too much time to toss and adjust the dll, it will lose the meaning of using the finished module. Some inexplicable errors may be that the definition is not standard, and errors occur across users/languages/environments. These inexplicable errors may not be found in a few days. The reason,
There are also some Linux/Windows/firmware developers who do not understand the architecture, which is a problem.

In hardware development, there is usually no concept of multithreading, let alone data security awareness. In his eyes, all code is executed sequentially, and all variables and memory addresses do not share the concept of conflict.

Linux usually does not have the meaning of multi-user cooperation, backward and backward compatibility, etc., because Linux programs are fragmented, regardless of backward and backward compatibility issues, Linux does not have operational friendliness, operational safety awareness, everything is a file, and resources are also desperately used, so what is usually done is relatively single-board, fortunately, there are many people, everyone puts an exe module into a function, like IOS, so limit it, you can't operate like this, you can't operate like that, and you can use a single function.

Windows, the problem is also very serious, but it's not good all of a sudden.

Pick these things, don't spray, why don't I make a system? It's simple to make a system, but it's impossible for you to operate it.
The driver must be developed, but the manufacturer has finished it, and the documentation cannot be found (for example, the PS2 keyboard interface, launched by IBM, but there is no standard documentation, everyone looks at the oscilloscope, analyzes the signal format, and then writes the firmware)
Windows right-click menu, the patent has not expired yet (it seems that there is still half a year left), Apple had a fight with Weiji back then, but it didn't fade away. Apple has been a one-button mouse for many years, and it also boasted about the benefits of one-button. Later, I made money selling the iphone, and the patent was about to expire. I exchanged patents with Microsoft (it seems that I still paid the money). Now in ocx, there are gradually a lot of right-click menus.

Some of the words are more long-winded, and I am afraid that many people don't understand, so I deliberately wrote more. Now many newcomers, most of them know web, py, java, etc., have no concept of low-level principles, and are unwilling to learn ("Many newcomers", not necessarily a few upstairs, don't divide the numbers, and then spray them indiscriminately)

stdcall, the WeChat I remember was used as a recommendation method in the official books or articles in the 1990s. This cannot be said to be an absolute standard, but for so many years, everyone has done DLL in this way, whether it is or not, it is explicitly stated to protect against errors under any circumstances and ensure forward and backward compatibility. It has long been an unwritten standard/rule.

Specifically, you can search for SDK packages provided by all decent companies, such as Haikang, Dahua, SDKs from these hardware manufacturers, all Microsoft H files, all cross-language Windows tools, etc.
Except for the code shift under Linux, such as opencv, ffmpeg, x264.faac, etc., is more messy, the specific value is determined by the default value of the compilation tool,
Even opencv can't see what the retrun value is from the api, whether to release it or not (the return value may actually be a memory block, but the return value, in the h file given to the user, is defined as int, not void *),
ffmpeg, from time to time, the structure has been changed out of order (define the ab member, and later want to add it, just plug in one and become acb), even if it is also developed in C and referenced directly, you cannot upgrade the module/function by copying the DLL/so method.

It is said that HWND is a DWORD, because for external users, he is a DWORD and does not need to be released. The user takes the value and assigns the value in the standard int32 method, and does not care about how it is implemented internally. It is similar to the void* returned by the author WKECRETEWEBVIEW. It doesn't matter if it is written as DWORD/Handle, anyway, it is impossible for the user to manipulate this data. If the user knows the word "Create", it must be released manually.,
When external users redefine the API, they can refer to the deer as the horse, as long as they ensure that the content and method of the API are the same when exchanging data.

Make up
wkeLoadURL, it seems that there is no function to automatically fill in http://, you need to provide the full address before you can
You can consider judging whether there is a '://'string, and whether you really want to make up the header. If you say it wrong, when I didn't say it

To reiterate, when crossing languages, inexplicable problems are found. It is likely that the author has insufficient experience and some places are not defined according to the rules. The problem is caused by the author's inexperience.