# kotlinx.serialization generates serializers as companion objects and keeps
# type information in annotations; R8 strips both without these rules, and the
# failure is a runtime SerializationException in a release build only.
-keepattributes *Annotation*, InnerClasses
-dontnote kotlinx.serialization.**

-keepclassmembers class za.co.theebuhlefoundation.data.api.dto.** {
    *** Companion;
}
-keepclasseswithmembers class za.co.theebuhlefoundation.data.api.dto.** {
    kotlinx.serialization.KSerializer serializer(...);
}

# Retrofit keeps interface method signatures by reflection.
-keep,allowobfuscation,allowshrinking interface retrofit2.Call
-keep,allowobfuscation,allowshrinking class retrofit2.Response
-keep,allowobfuscation,allowshrinking class kotlin.coroutines.Continuation
