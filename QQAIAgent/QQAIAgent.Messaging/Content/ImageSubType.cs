namespace QQAIAgent.Messaging.Content;

/// <summary>图片子类型，数值与 NapCat 的 PicSubType 定义一致。</summary>
public enum ImageSubType
{
    /// <summary>普通图片，对应 NapCat 的 KNORMAL。</summary>
    Normal = 0,
    /// <summary>自定义表情图片，对应 NapCat 的 KCUSTOM。</summary>
    Custom = 1,
    /// <summary>对应 NapCat 的 KHOT 图片分类，具体业务含义待确认。</summary>
    Hot = 2,
    /// <summary>对应 NapCat 的 KDIPPERCHART 图片分类，具体业务含义待确认。</summary>
    DipperChart = 3,
    /// <summary>对应 NapCat 的 KSMART 图片分类，具体业务含义待确认。</summary>
    Smart = 4,
    /// <summary>对应 NapCat 的 KSPACE 图片分类，具体业务含义待确认。</summary>
    Space = 5,
    /// <summary>未知图片子类型，对应 NapCat 的 KUNKNOW。</summary>
    Unknown = 6,
    /// <summary>对应 NapCat 的 KRELATED 图片分类，具体业务含义待确认。</summary>
    Related = 7
}
