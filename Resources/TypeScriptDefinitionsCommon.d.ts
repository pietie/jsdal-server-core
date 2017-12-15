declare module jsDAL {
    interface Sproc { }
}

interface ISprocExecGeneric0<O/*Output*/, U/*Parameters*/> {
    ExecQuery(parameters?: U): Promise<ApiResponseNoResult<O>>;
    ExecSingleResult(parameters?: U): Promise<ApiResponseNoResult<O>>;
    ExecNonQuery(parameters?: U): Promise<ApiResponseNoResult<O>>;
    then(any): Promise<any>;
    always(cb: (...any) => any): ISprocExecGeneric0<O, U>;
    Select(any): jsDAL.Sproc;
}

interface ISprocExecGeneric1<O/*Output*/, T/*Result set*/, U/*Parameter*/> {
    ExecQuery(parameters?: U): Promise<ApiResponse<O, T>>;
    ExecSingleResult(parameters?: U): Promise<ApiResponseSingleResult<O, T>>;
    ExecNonQuery(parameters?: U): Promise<ApiResponseNoResult<O>>;
    then<U>(onFulfilled?: (value: T) => U | Promise<U>, onRejected?: (error: any) => U | Promise<U>): Promise<U>;
    always(cb: (...any) => any): ISprocExecGeneric1<O, T, U>;
    Select(any): ISprocExecGeneric1<O, T, U>;
    captcha(captchaResponseValue: string): ISprocExecGeneric1<O, T, U>;
}

interface ISprocExecGeneric2<O, T1, T2, U> extends ISprocExecGeneric1<O, T1, U> { ExecQuery(parameters?: U): Promise<ApiResponse2<O, T1, T2>>; always(cb: (...any) => any): ISprocExecGeneric2<O, T1, T2, U>; }
interface ISprocExecGeneric3<O, T1, T2, T3, U> extends ISprocExecGeneric1<O, T1, U> { ExecQuery(parameters?: U): Promise<ApiResponse3<O, T1, T2, T3>>; }
interface ISprocExecGeneric4<O, T1, T2, T3, T4, U> extends ISprocExecGeneric1<O, T1, U> { ExecQuery(parameters?: U): Promise<ApiResponse4<O, T1, T2, T3, T4>>; }
interface ISprocExecGeneric5<O, T1, T2, T3, T4, T5, U> extends ISprocExecGeneric1<O, T1, U> { ExecQuery(parameters?: U): Promise<ApiResponse5<O, T1, T2, T3, T4, T5>>; }

interface ISprocExecGeneric6<O, T1, T2, T3, T4, T5, T6, U> extends ISprocExecGeneric1<O, T1, U> { ExecQuery(parameters?: U): Promise<ApiResponse6<O, T1, T2, T3, T4, T5, T6>>; }

interface ISprocExecGeneric7<O, T1, T2, T3, T4, T5, T6, T7, U> extends ISprocExecGeneric1<O, T1, U> { ExecQuery(parameters?: U): Promise<ApiResponse7<O, T1, T2, T3, T4, T5, T6, T7>>; }
interface ISprocExecGeneric8<O, T1, T2, T3, T4, T5, T6, T7, T8, U> extends ISprocExecGeneric1<O, T1, U> { ExecQuery(parameters?: U): Promise<ApiResponse8<O, T1, T2, T3, T4, T5, T6, T7, T8>>; }
interface ISprocExecGeneric9<O, T1, T2, T3, T4, T5, T6, T7, T8, T9, U> extends ISprocExecGeneric1<O, T1, U> { ExecQuery(parameters?: U): Promise<ApiResponse9<O, T1, T2, T3, T4, T5, T6, T7, T8, T9>>; }
interface ISprocExecGeneric10<O, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, U> extends ISprocExecGeneric1<O, T1, U> { ExecQuery(parameters?: U): Promise<ApiResponse10<O, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>>; }

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