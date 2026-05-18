using System.Collections.Generic;

namespace BrowserCore.Engine
{
    public enum HtmlTag
    {
        Unknown,
        // Document
        Document, Text,
        // Block
        Div, P, H1, H2, H3, H4, H5, H6,
        Article, Section, Nav, Header, Footer, Main,
        Aside, Figure, Figcaption, Blockquote, Pre, Address, Center,
        Form, Dialog, Details, Summary,
        // Table
        Table, Tr, Td, Th, Thead, Tbody, Tfoot, Col, Colgroup, Caption,
        // List
        Ul, Ol, Li, Dl, Dt, Dd,
        // Inline
        Span, A, Strong, B, Em, I, U, Small, Sub, Sup,
        Mark, Q, Cite, Kbd, Abbr, S, Del, Ins, Code,
        Br, Hr, Wbr,
        // Media
        Img, Picture, Video, Audio, Source, Svg, Canvas,
        FigureCaption, FigCaption,
        // Form
        Input, Textarea, Select, Option, Optgroup,
        Button, Label, Fieldset, Legend, Output,
        Progress, Meter,
        // Head
        Head, Title, Meta, Link, Style, Script, Noscript, Base,
        // Embedded
        Iframe, Embed, Object, Param,
        // Misc
        Html, Body, Template, Slot, Time, Data,
        Ruby, Rt, Rp, Bdi, Bdo, WbrTag,
        // Legacy
        Font, Center2, Strike, Big, Tt, U2, S2, Menu, Dir,
    }

    public static class HtmlTagLookup
    {
        private static readonly Dictionary<string, HtmlTag> _map = new Dictionary<string, HtmlTag>(System.StringComparer.OrdinalIgnoreCase);

        static HtmlTagLookup()
        {
            _map["#document"] = HtmlTag.Document;
            _map["#text"] = HtmlTag.Text;
            _map["div"] = HtmlTag.Div;
            _map["p"] = HtmlTag.P;
            _map["h1"] = HtmlTag.H1;
            _map["h2"] = HtmlTag.H2;
            _map["h3"] = HtmlTag.H3;
            _map["h4"] = HtmlTag.H4;
            _map["h5"] = HtmlTag.H5;
            _map["h6"] = HtmlTag.H6;
            _map["article"] = HtmlTag.Article;
            _map["section"] = HtmlTag.Section;
            _map["nav"] = HtmlTag.Nav;
            _map["span"] = HtmlTag.Span;
            _map["a"] = HtmlTag.A;
            _map["strong"] = HtmlTag.Strong;
            _map["b"] = HtmlTag.B;
            _map["em"] = HtmlTag.Em;
            _map["i"] = HtmlTag.I;
            _map["u"] = HtmlTag.U;
            _map["small"] = HtmlTag.Small;
            _map["sub"] = HtmlTag.Sub;
            _map["sup"] = HtmlTag.Sup;
            _map["mark"] = HtmlTag.Mark;
            _map["q"] = HtmlTag.Q;
            _map["cite"] = HtmlTag.Cite;
            _map["kbd"] = HtmlTag.Kbd;
            _map["abbr"] = HtmlTag.Abbr;
            _map["s"] = HtmlTag.S;
            _map["del"] = HtmlTag.Del;
            _map["ins"] = HtmlTag.Ins;
            _map["code"] = HtmlTag.Code;
            _map["br"] = HtmlTag.Br;
            _map["hr"] = HtmlTag.Hr;
            _map["img"] = HtmlTag.Img;
            _map["picture"] = HtmlTag.Picture;
            _map["video"] = HtmlTag.Video;
            _map["audio"] = HtmlTag.Audio;
            _map["source"] = HtmlTag.Source;
            _map["svg"] = HtmlTag.Svg;
            _map["canvas"] = HtmlTag.Canvas;
            _map["input"] = HtmlTag.Input;
            _map["textarea"] = HtmlTag.Textarea;
            _map["select"] = HtmlTag.Select;
            _map["option"] = HtmlTag.Option;
            _map["optgroup"] = HtmlTag.Optgroup;
            _map["button"] = HtmlTag.Button;
            _map["label"] = HtmlTag.Label;
            _map["fieldset"] = HtmlTag.Fieldset;
            _map["legend"] = HtmlTag.Legend;
            _map["output"] = HtmlTag.Output;
            _map["progress"] = HtmlTag.Progress;
            _map["meter"] = HtmlTag.Meter;
            _map["table"] = HtmlTag.Table;
            _map["tr"] = HtmlTag.Tr;
            _map["td"] = HtmlTag.Td;
            _map["th"] = HtmlTag.Th;
            _map["thead"] = HtmlTag.Thead;
            _map["tbody"] = HtmlTag.Tbody;
            _map["tfoot"] = HtmlTag.Tfoot;
            _map["col"] = HtmlTag.Col;
            _map["colgroup"] = HtmlTag.Colgroup;
            _map["caption"] = HtmlTag.Caption;
            _map["ul"] = HtmlTag.Ul;
            _map["ol"] = HtmlTag.Ol;
            _map["li"] = HtmlTag.Li;
            _map["dl"] = HtmlTag.Dl;
            _map["dt"] = HtmlTag.Dt;
            _map["dd"] = HtmlTag.Dd;
            _map["form"] = HtmlTag.Form;
            _map["dialog"] = HtmlTag.Dialog;
            _map["details"] = HtmlTag.Details;
            _map["summary"] = HtmlTag.Summary;
            _map["head"] = HtmlTag.Head;
            _map["title"] = HtmlTag.Title;
            _map["meta"] = HtmlTag.Meta;
            _map["link"] = HtmlTag.Link;
            _map["style"] = HtmlTag.Style;
            _map["script"] = HtmlTag.Script;
            _map["noscript"] = HtmlTag.Noscript;
            _map["base"] = HtmlTag.Base;
            _map["html"] = HtmlTag.Html;
            _map["body"] = HtmlTag.Body;
            _map["template"] = HtmlTag.Template;
            _map["slot"] = HtmlTag.Slot;
            _map["iframe"] = HtmlTag.Iframe;
            _map["embed"] = HtmlTag.Embed;
            _map["object"] = HtmlTag.Object;
            _map["param"] = HtmlTag.Param;
            _map["header"] = HtmlTag.Header;
            _map["footer"] = HtmlTag.Footer;
            _map["main"] = HtmlTag.Main;
            _map["aside"] = HtmlTag.Aside;
            _map["figure"] = HtmlTag.Figure;
            _map["figcaption"] = HtmlTag.Figcaption;
            _map["blockquote"] = HtmlTag.Blockquote;
            _map["pre"] = HtmlTag.Pre;
            _map["address"] = HtmlTag.Address;
            _map["center"] = HtmlTag.Center;
            _map["time"] = HtmlTag.Time;
            _map["data"] = HtmlTag.Data;
            _map["ruby"] = HtmlTag.Ruby;
            _map["rt"] = HtmlTag.Rt;
            _map["rp"] = HtmlTag.Rp;
            _map["bdi"] = HtmlTag.Bdi;
            _map["bdo"] = HtmlTag.Bdo;
            _map["font"] = HtmlTag.Font;
            _map["strike"] = HtmlTag.Strike;
            _map["big"] = HtmlTag.Big;
            _map["tt"] = HtmlTag.Tt;
            _map["menu"] = HtmlTag.Menu;
            _map["dir"] = HtmlTag.Dir;
            _map["wbr"] = HtmlTag.Wbr;
        }

        public static HtmlTag FromString(string tag)
        {
            if (string.IsNullOrEmpty(tag)) return HtmlTag.Unknown;
            HtmlTag result;
            return _map.TryGetValue(tag, out result) ? result : HtmlTag.Unknown;
        }
    }
}
