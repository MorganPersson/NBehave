﻿using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.Text;

namespace NBehave.VS2010.Plugin.GherkinFileEditor
{
    public class ParserEvent
    {
        public ParserEvent(ParserEventType eventType)
        {
            EventType = eventType;
        }

        public ParserEventType EventType { get; set; }

        public string Keyword { get; set; }

        public string Title { get; set; }

        public string Description { get; set; }

        public int Line { get; set; }

        public string Name { get; set; }

        public string Text { get; set; }

        public IEnumerable<string> List { get; set; }

        public string Comment { get; set; }

        public string Content { get; set; }

        public bool Eof { get; set; }

        public Span KeywordSpan { get; set; }

        public Span TitleSpan { get; set; }

        public Span DescriptionSpan { get; set; }
    }
}