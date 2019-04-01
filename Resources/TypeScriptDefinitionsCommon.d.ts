declare module jsDAL {
    interface Sproc {
     }
}

interface IDALConfig {
    AutoSetTokenGuid?: boolean;
    AutoProcessApiResponse?: boolean;
    HandleExceptions?: boolean;
    ShowPageLoadingIndicator?: boolean;
    CommandTimeoutInSeconds?: number;
    $select?: string;
    $captcha?: string;
    HttpMethod?: string;
}


interface ISprocExecGeneric0<O/*Output*/, U/*Parameters*/> {
    configure(config: IDALConfig): ISprocExecGeneric0<O, U>;
    afterExec(cb: (...any) => any): ISprocExecGeneric0<O, U>;
    always(cb: (...fn: any[]) => any): ISprocExecGeneric0<O, U>;
    captcha(captchaResponseValue: string): ISprocExecGeneric0<O, U>;
    Select(...cols: string[]): ISprocExecGeneric0<O, U>;
    ExecQuery(parameters?: U): Promise<ApiResponseNoResult<O>>;
    ExecSingleResult(parameters?: U): Promise<ApiResponseNoResult<O>>;
    ExecNonQuery(parameters?: U): Promise<ApiResponseNoResult<O>>;
}

interface ISprocExecGeneric1<O/*Output*/, T1/*Result set*/, U/*Parameter*/> {
    configure(config?: IDALConfig): ISprocExecGeneric1<O, T1, U>;
    afterExec(cb: (...any) => any): ISprocExecGeneric1<O, T1, U>;
    always(cb: (...fn: any[]) => any): ISprocExecGeneric1<O, T1, U>;
    captcha(captchaResponseValue: string): ISprocExecGeneric1<O, T1, U>;
    Select(...cols: string[]): ISprocExecGeneric1<O, T1, U>;
    ExecQuery(parameters?: U): Promise<ApiResponse<O, T1>>;
    ExecSingleResult(parameters?: U): Promise<ApiResponseSingleResult<O, T1>>;
    ExecNonQuery(parameters?: U): Promise<ApiResponseNoResult<O>>;
}

interface ISprocExecGeneric2<O/*Output*/, T1, T2, U/*Parameter*/> {
    configure(config?: IDALConfig): ISprocExecGeneric2<O, T1, T2, U>;
    afterExec(cb: (...any) => any): ISprocExecGeneric2<O, T1, T2, U>;
    always(cb: (...fn: any[]) => any): ISprocExecGeneric2<O, T1, T2, U>;
    captcha(captchaResponseValue: string): ISprocExecGeneric2<O, T1, T2, U>;
    Select(...cols: string[]): ISprocExecGeneric2<O, T1, T2, U>;
    ExecQuery(parameters?: U): Promise<ApiResponse2<O, T1, T2>>;
    ExecSingleResult(parameters?: U): Promise<ApiResponseSingleResult<O, T1>>;
    ExecNonQuery(parameters?: U): Promise<ApiResponseNoResult<O>>;
}

interface ISprocExecGeneric3<O, T1, T2, T3, U> {
    configure(config?: IDALConfig): ISprocExecGeneric3<O, T1, T2, T3, U>;
    afterExec(cb: (...any) => any): ISprocExecGeneric3<O, T1, T2, T3, U>;
    always(cb: (...fn: any[]) => any): ISprocExecGeneric3<O, T1, T2, T3, U>;
    captcha(captchaResponseValue: string): ISprocExecGeneric3<O, T1, T2, T3, U>;
    Select(...cols: string[]): ISprocExecGeneric3<O, T1, T2, T3, U>;
    ExecQuery(parameters?: U): Promise<ApiResponse3<O, T1, T2, T3>>;
    ExecSingleResult(parameters?: U): Promise<ApiResponseSingleResult<O, T1>>;
    ExecNonQuery(parameters?: U): Promise<ApiResponseNoResult<O>>;
}

interface ISprocExecGeneric4<O, T1, T2, T3, T4, U> {
    configure(config?: IDALConfig): ISprocExecGeneric4<O, T1, T2, T3, T4, U>;
    afterExec(cb: (...any) => any): ISprocExecGeneric4<O, T1, T2, T3, T4, U>;
    always(cb: (...fn: any[]) => any): ISprocExecGeneric4<O, T1, T2, T3, T4, U>;
    captcha(captchaResponseValue: string): ISprocExecGeneric4<O, T1, T2, T3, T4, U>;
    Select(...cols: string[]): ISprocExecGeneric4<O, T1, T2, T3, T4, U>;
    ExecQuery(parameters?: U): Promise<ApiResponse4<O, T1, T2, T3, T4>>;
    ExecSingleResult(parameters?: U): Promise<ApiResponseSingleResult<O, T1>>;
    ExecNonQuery(parameters?: U): Promise<ApiResponseNoResult<O>>;
}

interface ISprocExecGeneric5<O, T1, T2, T3, T4, T5, U> {
    configure(config?: IDALConfig): ISprocExecGeneric5<O, T1, T2, T3, T4, T5, U>;
    afterExec(cb: (...any) => any): ISprocExecGeneric5<O, T1, T2, T3, T4, T5, U>;
    always(cb: (...fn: any[]) => any): ISprocExecGeneric5<O, T1, T2, T3, T4, T5, U>;
    captcha(captchaResponseValue: string): ISprocExecGeneric5<O, T1, T2, T3, T4, T5, U>;
    Select(...cols: string[]): ISprocExecGeneric5<O, T1, T2, T3, T4, T5, U>;
    ExecQuery(parameters?: U): Promise<ApiResponse5<O, T1, T2, T3, T4, T5>>;
    ExecSingleResult(parameters?: U): Promise<ApiResponseSingleResult<O, T1>>;
    ExecNonQuery(parameters?: U): Promise<ApiResponseNoResult<O>>;
}

interface ISprocExecGeneric6<O, T1, T2, T3, T4, T5, T6, U> {
    configure(config?: IDALConfig): ISprocExecGeneric6<O, T1, T2, T3, T4, T5, T6, U>;
    afterExec(cb: (...any) => any): ISprocExecGeneric6<O, T1, T2, T3, T4, T5, T6, U>;
    always(cb: (...fn: any[]) => any): ISprocExecGeneric6<O, T1, T2, T3, T4, T5, T6, U>;
    captcha(captchaResponseValue: string): ISprocExecGeneric6<O, T1, T2, T3, T4, T5, T6, U>;
    Select(...cols: string[]): ISprocExecGeneric6<O, T1, T2, T3, T4, T5, T6, U>;
    ExecQuery(parameters?: U): Promise<ApiResponse6<O, T1, T2, T3, T4, T5, T6>>;
    ExecSingleResult(parameters?: U): Promise<ApiResponseSingleResult<O, T1>>;
    ExecNonQuery(parameters?: U): Promise<ApiResponseNoResult<O>>;
}

interface ISprocExecGeneric7<O, T1, T2, T3, T4, T5, T6, T7, U> {
    configure(config?: IDALConfig): ISprocExecGeneric7<O, T1, T2, T3, T4, T5, T6, T7, U>;
    afterExec(cb: (...any) => any): ISprocExecGeneric7<O, T1, T2, T3, T4, T5, T6, T7, U>;
    always(cb: (...fn: any[]) => any): ISprocExecGeneric7<O, T1, T2, T3, T4, T5, T6, T7, U>;
    captcha(captchaResponseValue: string): ISprocExecGeneric7<O, T1, T2, T3, T4, T5, T6, T7, U>;
    Select(...cols: string[]): ISprocExecGeneric7<O, T1, T2, T3, T4, T5, T6, T7, U>;
    ExecQuery(parameters?: U): Promise<ApiResponse7<O, T1, T2, T3, T4, T5, T6, T7>>;
    ExecSingleResult(parameters?: U): Promise<ApiResponseSingleResult<O, T1>>;
    ExecNonQuery(parameters?: U): Promise<ApiResponseNoResult<O>>;
}

interface ISprocExecGeneric8<O, T1, T2, T3, T4, T5, T6, T7, T8, U> {
    configure(config?: IDALConfig): ISprocExecGeneric8<O, T1, T2, T3, T4, T5, T6, T7, T8, U>;
    afterExec(cb: (...any) => any): ISprocExecGeneric8<O, T1, T2, T3, T4, T5, T6, T7, T8, U>;
    always(cb: (...fn: any[]) => any): ISprocExecGeneric8<O, T1, T2, T3, T4, T5, T6, T7, T8, U>;
    captcha(captchaResponseValue: string): ISprocExecGeneric8<O, T1, T2, T3, T4, T5, T6, T7, T8, U>;
    Select(...cols: string[]): ISprocExecGeneric8<O, T1, T2, T3, T4, T5, T6, T7, T8, U>;
    ExecQuery(parameters?: U): Promise<ApiResponse8<O, T1, T2, T3, T4, T5, T6, T7, T8>>;
    ExecSingleResult(parameters?: U): Promise<ApiResponseSingleResult<O, T1>>;
    ExecNonQuery(parameters?: U): Promise<ApiResponseNoResult<O>>;
}

interface ISprocExecGeneric9<O, T1, T2, T3, T4, T5, T6, T7, T8, T9, U> {
    configure(config?: IDALConfig): ISprocExecGeneric9<O, T1, T2, T3, T4, T5, T6, T7, T8, T9, U>;
    afterExec(cb: (...any) => any): ISprocExecGeneric9<O, T1, T2, T3, T4, T5, T6, T7, T8, T9, U>;
    always(cb: (...fn: any[]) => any): ISprocExecGeneric9<O, T1, T2, T3, T4, T5, T6, T7, T8, T9, U>;
    captcha(captchaResponseValue: string): ISprocExecGeneric9<O, T1, T2, T3, T4, T5, T6, T7, T8, T9, U>;
    Select(...cols: string[]): ISprocExecGeneric9<O, T1, T2, T3, T4, T5, T6, T7, T8, T9, U>;
    ExecQuery(parameters?: U): Promise<ApiResponse9<O, T1, T2, T3, T4, T5, T6, T7, T8, T9>>;
    ExecSingleResult(parameters?: U): Promise<ApiResponseSingleResult<O, T1>>;
    ExecNonQuery(parameters?: U): Promise<ApiResponseNoResult<O>>;
}

interface ISprocExecGeneric10<O, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, U> {
    configure(config?: IDALConfig): ISprocExecGeneric10<O, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, U>;
    afterExec(cb: (...any) => any): ISprocExecGeneric10<O, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, U>;
    always(cb: (...fn: any[]) => any): ISprocExecGeneric10<O, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, U>;
    captcha(captchaResponseValue: string): ISprocExecGeneric10<O, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, U>;
    Select(...cols: string[]): ISprocExecGeneric10<O, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, U>;
    ExecQuery(parameters?: U): Promise<ApiResponse10<O, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>>;
    ExecSingleResult(parameters?: U): Promise<ApiResponseSingleResult<O, T1>>;
    ExecNonQuery(parameters?: U): Promise<ApiResponseNoResult<O>>;
}

interface IUDFExecGeneric<T, U> { Exec(parameters?: U): Promise<T>; }

declare enum ApiResponseType {
    Unknown = 0,
    Success = 1,
    InfoMsg = 10,
    ExclamationModal = 20,
    Error = 30,
    Exception = 40
}


interface ApiResponseBase<O/*Output*/, T/*Result Type*/> {
    Message?: string;
    Title?: string;
    Type?: ApiResponseType;
}


interface ApiResponse<O/*Output*/, T/*Result Type*/> extends ApiResponseBase<O, T> {
    Data?: { Table0?: Array<T>, OutputParms?: O };
}

interface ApiResponseNoResult<O> extends ApiResponseBase<O, void> { Data?: { OutputParms?: O } }

interface ApiResponseSingleResult<O, T> { Data?: { Result: T, OutputParms?: O } }
interface ApiResponse2<O, T0, T1> { Data?: { Table0?: T0[], Table1?: T1[], OutputParms?: O }; }
interface ApiResponse3<O, T0, T1, T2> { Data?: { Table0?: T0[], Table1?: T1[], Table2: T2[], OutputParms?: O }; }
interface ApiResponse4<O, T0, T1, T2, T3> { Data?: { Table0?: T0[], Table1?: T1[], Table2: T2[], Table3: T3[], OutputParms?: O }; }
interface ApiResponse5<O, T0, T1, T2, T3, T4> { Data?: { Table0?: T0[], Table1?: T1[], Table2: T2[], Table3: T3[], Table4: T4[], OutputParms?: O }; }
interface ApiResponse6<O, T0, T1, T2, T3, T4, T5> { Data?: { Table0?: T0[], Table1?: T1[], Table2: T2[], Table3: T3[], Table4: T4[], Table5: T5[], OutputParms?: O }; }
interface ApiResponse7<O, T0, T1, T2, T3, T4, T5, T6> { Data?: { Table0?: T0[], Table1?: T1[], Table2: T2[], Table3: T3[], Table4: T4[], Table5: T5[], Table6: T6[], OutputParms?: O }; }
interface ApiResponse8<O, T0, T1, T2, T3, T4, T5, T6, T7> { Data?: { Table0?: T0[], Table1?: T1[], Table2: T2[], Table3: T3[], Table4: T4[], Table5: T5[], Table6: T6[], Table7: T7[], OutputParms?: O }; }
interface ApiResponse9<O, T0, T1, T2, T3, T4, T5, T6, T7, T8> { Data?: { Table0?: T0[], Table1?: T1[], Table2: T2[], Table3: T3[], Table4: T4[], Table5: T5[], Table6: T6[], Table7: T7[], Table8: T8[], OutputParms?: O }; }
interface ApiResponse10<O, T0, T1, T2, T3, T4, T5, T6, T7, T8, T9> { Data?: { Table0?: T0[], Table1?: T1[], Table2: T2[], Table3: T3[], Table4: T4[], Table5: T5[], Table6: T6[], Table7: T7[], Table8: T8[], Table9: T9[], OutputParms?: O }; }